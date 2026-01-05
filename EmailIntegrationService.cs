namespace ReadyDOS.Example.Integration;

using CsvHelper;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using ReadyDOS.Example.Integration.Models;
using S3mphony;
using S3mphony.ModelSelector.Utility;
using S3mphony.Utility;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Globalization;
using System.Net;

/// <summary>
/// Provides functionality for generating AI/ML personalized product recommendations 
/// and sending marketing emails to recipients using a SendGrid integration.
/// </summary>
/// <remarks>This service coordinates the process of selecting the best-performing machine learning model,
/// generating product recommendations for recipients, and sending personalized marketing emails in batches. It is
/// designed to support scalable, data-driven email marketing campaigns and relies on external dependencies for storage,
/// logging, and email delivery. Thread safety and error handling are managed internally; consumers should ensure that
/// provided dependencies are properly configured.</remarks>
/// <param name="logger">The logger used to record informational, warning, and error messages during email integration operations. Cannot be
/// null.</param>
/// <param name="s3StorageUtility">The storage utility used to access and download machine learning model metrics from S3 storage. Cannot be null.</param>
/// <param name="s3ModelMetricChannel">The channel used to retrieve recent model metrics from S3 storage for candidate selection. Cannot be null.</param>
/// <param name="sendGridClientOptions">The configuration options for the SendGrid client, including the API key required for sending emails. Cannot be
/// null.</param>
public class EmailIntegrationService(
    ILogger logger,
    S3StorageUtility<ModelMetric> s3StorageUtility,
    S3Channel<ModelMetric> s3ModelMetricChannel,
    SendGridClientOptions sendGridClientOptions) {

    private readonly S3StorageUtility<ModelMetric> _s3Utility = s3StorageUtility;
    private readonly S3Channel<ModelMetric> _s3Channel = s3ModelMetricChannel;

    /// <summary>
    /// Downloads an AI/ML product recommendation model from S3 and uses it to send batch personalized marketing emails 
    /// in this example, product recommendations,
    /// to a list of recipients using the specified SendGrid 'template' (SendGrid feature).
    /// </summary>
    /// <remarks>This method discovers candidate models, selects the best-performing model, generates product
    /// recommendations for each recipient, and sends marketing emails in batches using SendGrid. The method logs
    /// progress and errors during the operation. Email sending is performed in batches to comply with typical email
    /// provider limits and best practices.</remarks>
    /// <param name="sendGridTemplateId">The identifier of the SendGrid email template to use for composing the marketing emails. Cannot be null or
    /// empty.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">Thrown if no recipients are found to send marketing emails to.</exception>
    public async Task EmailMarketing(string sendGridTemplateId) {
        try {
            List<ModelMetric> candidates =
                (await DiscoverCandidatesAsync(CancellationToken.None)).ToList();

            if (!candidates.Any())
                return;

            RankedModel? admired = ModelPicker.PickBest(candidates);
            if (admired is null) {
                logger.LogWarning("No admired model found.");
                return;
            }

            List<Recipient> recipients = await LoadRecipientsAsync();
            if (recipients.Count == 0) {
                throw new NotSupportedException("No recipients found.");
            }

            SendGridClient sendGridClient = new(sendGridClientOptions.ApiKey);

            // Keep batches reasonable
            // (SendGrid helper/examples often show batching;
            // your account limits vary).
            const int batchSize = 900;
            int totalSent = 0;

            // From catalog, database, etc.
            List<int> candidateSkus = await LoadSkusAsync();

            // Top 5 product recommendations per recipient
            Dictionary<string, List<(int sku, float score)>> recsByEmail =
                await RecommendTopProductsForRecipients(
                    admired, recipients, candidateSkus, topN: 5, CancellationToken.None);

            // Send marketing emails in batches (typical CANSPAM compliance)
            foreach (Recipient[]? batch in recipients.Chunk(batchSize)) {

                // Send the personalized email with customer-specific product recommendations
                SendGridMessage message = 
                    BuildSendGridMessagePersonalized(batch, admired, sendGridTemplateId, recsByEmail);

                Response resp = await sendGridClient.SendEmailAsync(message);

                if (resp.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK) {
                    totalSent += batch.Length;
                    logger.LogInformation($"SendGrid accepted batch of {batch.Length}. Total accepted so far: {totalSent}");
                } else {
                    string body = await resp.Body.ReadAsStringAsync();
                    logger.LogError($"SendGrid error: {(int)resp.StatusCode} {resp.StatusCode}");
                    logger.LogError(body);
                    return;
                }
            }

            logger.LogInformation($"Done. Total recipients accepted for sending: {totalSent}");
        } catch (Exception ex) {
            logger.LogCritical(ex, "Email marketing failed. See inner exception.");
        }
    }

    /// <summary>
    /// Asynchronously retrieves a collection of the most recent machine learning model metrics from the storage source.
    /// </summary>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains an enumerable collection of the most
    /// recent model metrics.</returns>
    private async Task<IEnumerable<ModelMetric>> DiscoverCandidatesAsync(CancellationToken ct) {
        const string _modelsS3DirectoryPrefix = "models/";

        // Get the 100 most recent ML model metrics to limit scope
        return await _s3Channel.GetRecentStructures(100, _modelsS3DirectoryPrefix, ct);
    }

    /// <summary>
    /// Builds a SendGridMessage with personalized content for each recipient in the specified batch, using the provided
    /// template and recommendations.
    /// </summary>
    /// <remarks>Each recipient in the batch receives a separate personalization entry in the message, with
    /// template data including their first name, segment, model information, and a list of recommendations. The method
    /// assumes that the specified SendGrid template is compatible with the provided personalization data.</remarks>
    /// <param name="batch">The collection of recipients to include in the message. Each recipient will receive a personalized email based
    /// on their information and recommendations.</param>
    /// <param name="chosenModel">The ranked model used to generate personalization data for the message, including model file and score
    /// information.</param>
    /// <param name="sendGridTemplateId">The identifier of the SendGrid dynamic template to use for the message. Cannot be null, empty, or whitespace.</param>
    /// <param name="recommendationsByEmail">A dictionary mapping recipient email addresses to a list of recommended SKUs and their associated scores. Used
    /// to personalize the recommendations section for each recipient.</param>
    /// <returns>A SendGridMessage instance containing personalized content for each recipient in the batch.</returns>
    /// <exception cref="Exception">Thrown if sendGridTemplateId is null, empty, or consists only of whitespace.</exception>
    private static SendGridMessage BuildSendGridMessagePersonalized(
        IEnumerable<Recipient> batch,
        RankedModel chosenModel,
        string sendGridTemplateId,
        Dictionary<string, List<(int sku, float score)>> recommendationsByEmail) {

        if (string.IsNullOrWhiteSpace(sendGridTemplateId))
            throw new Exception("SendGrid template usage is assumed.");

        SendGridMessage message = new SendGridMessage();
        message.SetFrom(new EmailAddress("marketing@yourcompany.com", "Your Company"));
        message.SetTemplateId(sendGridTemplateId);

        foreach (Recipient recipient in batch) {
            Personalization personalization = new();
            personalization.Tos.Add(
                new EmailAddress(
                    recipient.Email,
                    $"{recipient.FirstName} {recipient.LastName}".Trim()));

            recommendationsByEmail.TryGetValue(recipient.Email, out var recs);
            recs ??= new();

            personalization.TemplateData = (new {
                firstName = recipient.FirstName,
                segment = recipient.Segment,
                modelFile = chosenModel.Metric.FileName,
                modelScore = chosenModel.Score,
                recommendations = recs.Select(x => new { sku = x.sku, score = x.score }).ToList()
            });

            message.Personalizations.Add(personalization);
        }

        return message;
    }


    /// <summary>
    /// Generates personalized product recommendations for a set of recipients using the specified ranking model.
    /// </summary>
    /// <remarks>Recipients without a valid email address are excluded from the results. The method performs
    /// batch scoring for efficiency. The returned scores represent the model's confidence or relevance for each
    /// recommended product.</remarks>
    /// <param name="chosenModel">The ranking model to use for generating product recommendations.</param>
    /// <param name="recipients">The list of recipients for whom recommendations will be generated. Each recipient should have a valid customer
    /// identifier.</param>
    /// <param name="skus">The list of product SKUs to consider for recommendations.</param>
    /// <param name="topN">The maximum number of top recommendations to return for each recipient. Must be greater than zero.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A dictionary mapping each recipient's email address to a list of tuples containing the recommended product SKU
    /// and its associated score. The list contains up to the specified number of top recommendations per recipient. If
    /// no recommendations are available, the dictionary is empty.</returns>
    private async Task<Dictionary<string, List<(int sku, float score)>>>
        RecommendTopProductsForRecipients(
            RankedModel chosenModel,
            List<Recipient> recipients,
            List<int> skus,
            int topN,
            CancellationToken ct) {

        byte[] modelBytes = await _s3Utility.DownloadBytesAsync(chosenModel.Metric.FileName);
        (MLContext mlContext, ITransformer model, _) = LoadModel(modelBytes);

        var rows = new List<RecommendationInput>(recipients.Count * skus.Count);

        foreach (Recipient recipient in recipients) {
            if (!string.IsNullOrEmpty(recipient.Email))
                continue;

            foreach (int sku in skus) {
                rows.Add(new RecommendationInput {
                    CustomerId = recipient.CustomerId,
                    Sku = sku
                });
            }
        }

        if (rows.Count == 0)
            return new();

        // Transform (batch scoring)
        IDataView dataView = mlContext.Data.LoadFromEnumerable(rows);
        IDataView scored = model.Transform(dataView);

        // Materialize
        List<ScoredRow> scoredRows =
            mlContext.Data
                .CreateEnumerable<ScoredRow>(scored, reuseRowObject: false)
                .ToList();

        // Group by recipient --> top N
        // Put email back in; easiest is to include Email in input rows too
        // Example data is lacking for demonstration purposes
        return scoredRows
            .GroupBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.Score)
                    .Take(topN)
                    .Select(x => (x.Sku, x.Score))
                    .ToList(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    /// <summary>
    /// Loads an ML.NET model from the specified byte array and returns the associated MLContext, model, and input
    /// schema.
    /// </summary>
    /// <param name="modelBytes">A byte array containing the serialized ML.NET model data. Cannot be null.</param>
    /// <returns>A tuple containing the MLContext used for loading, the loaded ITransformer model, and the DataViewSchema
    /// representing the model's expected input schema.</returns>
    private static (MLContext ml, ITransformer model, DataViewSchema schema) LoadModel(byte[] modelBytes) {
        MLContext ml = new();
        using var ms = new MemoryStream(modelBytes);
        ITransformer model = ml.Model.Load(ms, out DataViewSchema schema);
        return (ml, model, schema);
    }

    /// <summary>
    /// Asynchronously loads a list of SKU identifiers from a CSV file.
    /// </summary>
    /// <remarks>The method expects the CSV file to be named "FakeEmails.csv" and located in the application's
    /// working directory. Each record in the file should be convertible to an integer SKU identifier. If the file does
    /// not exist or contains invalid data, an exception may be thrown.</remarks>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of integers representing the
    /// SKU identifiers loaded from the CSV file.</returns>
    private static async Task<List<int>> LoadSkusAsync() {
        using (StreamReader reader = new("./FakeEmails.csv"))
        using (CsvReader csv = new(reader, CultureInfo.InvariantCulture)) {
            IEnumerable<int> skus = csv.GetRecords<int>();
            return skus.ToList();
        }
    }

    /// <summary>
    /// Asynchronously loads a list of recipients from a CSV file.
    /// </summary>
    /// <remarks>The method expects the CSV file to be named "FakeEmails.csv" and located in the application's
    /// working directory. Each record in the file should contain an integer ID and an email address. Default values are
    /// assigned to other recipient fields.</remarks>
    /// <returns>A task that represents the asynchronous operation. The task result contains a list of <see cref="Recipient"/>
    /// objects loaded from the CSV file.</returns>
    private static async Task<List<Recipient>> LoadRecipientsAsync() {
        using (StreamReader reader = new("./FakeEmails.csv"))
        using (CsvReader csv = new(reader, CultureInfo.InvariantCulture)) {
            IEnumerable<(int id, string email)> records = csv.GetRecords<(int, string)>();

            return [.. records.Select(r => new Recipient(
                Email: r.email,
                CustomerId: r.id,
                FirstName: "Valued",
                LastName: "Customer",
                Segment: "Example"
            ))];
        }
    }
}

