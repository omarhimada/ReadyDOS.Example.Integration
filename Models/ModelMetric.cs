using Microsoft.ML.Data;
using S3mphony.ModelSelector;
using System.Globalization;

namespace ReadyDOS.Example.Integration.Models {
    /// <summary>
    /// Represents a set of evaluation metrics for a trained machine learning model, including classification and
    /// regression statistics, model identity, and training metadata.
    /// </summary>
    /// <remarks>This record provides a unified view of key performance metrics for both classification and
    /// regression models. It includes properties for common metrics such as accuracy, AUC, F1 score, R-squared, and
    /// error measures, as well as information about the model and the time it was trained. Use this type to store or
    /// report model evaluation results in workflows that require consistent metric representation across different
    /// model types.</remarks>
    public sealed record ModelMetric : IWorkflowMetric {
        public Guid Id { get; set; }
        public string ModelName { get; set; }
        public string FileName { get; set; }
        public MulticlassClassificationMetrics? Multiclass { get; set; }
        public double? RSquared { get; set; }
        public double? RMSE { get; set; }
        public double? MeanAbsoluteError { get; set; }
        public double? MeanSquaredError { get; set; }
        public double? LossFunction { get; set; }
        public double? AUC { get; set; }
        public double? Accuracy { get; set; }
        public double? F1Score { get; set; }
        public double? Precision { get; set; }
        public double? Recall { get; set; }
        public double? LogLoss { get; set; }
        public double? LogLossReduction { get; set; }
        public bool? Binary { get; set; }
        public DateTime TrainedAtUtc { get; set; }

        public string ToSummaryString() {
            var parts = new List<string>();
            if (AUC is not null)
                parts.Add($"AUC = {AUC.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
            if (RSquared is not null)
                parts.Add($"R² = {RSquared.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
            if (RMSE is not null)
                parts.Add($"RMSE = {RMSE.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
            parts.Add($"At = {TrainedAtUtc:O}");
            return parts.Count == 0 ? "(no metrics)" : string.Join(", ", parts);
        }
    }
}
