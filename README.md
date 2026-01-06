# ReadyDOS Integration Example
## Email Marketing with ML Recommendations 
This repository demonstrates a **production-style, end-to-end integration example** combining:

- **Automated ML model discovery and selection** from S3 (via S3mphony Model Selector utilities)
- **ML.NET recommendation inference** to generate personalized product suggestions per customer
- **SendGrid Dynamic Templates** for scalable, batch-delivered marketing emails (i.e.: typical integration process with SendGrid)
- The goal is to demonstrate the E2E ML process, it is one thing to train a model and collect metrics, it is another thing to actualize business value with it.
My personal goal is to make as much of this automatic as possible.

---

## What This Example Covers

| Stage | Capability Demonstrated |
|---|---|
| Model Discovery | Retrieves the most recent candidate models from an S3 prefix |
| Model Admiration | Picks the best model based on evaluation metrics (AUC, R², RMSE, etc.) |
| Recommendation Inference | Scores `(CustomerId, SKU)` pairs and extracts **Top-N products per customer** |
| Email Personalization | Injects recommendations into per-recipient template data via SendGrid Personalizations |
| Batch Delivery | Sends large campaigns in controlled chunks (e.g.: 900 recipients per batch) |
| Business Value Actualization | Converts ML signals into marketing content that users actually receive |

---

## Note
This example demonstrates a chosen set of recipients by humans, and then the ML model chooses the products to advertise to them.
With the pre-step of segmentation, not shown in this example integration, that process becomes much more automated.
