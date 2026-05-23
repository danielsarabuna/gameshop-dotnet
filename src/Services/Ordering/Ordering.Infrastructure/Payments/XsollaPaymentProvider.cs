using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;
using DomainPaymentStatus = Ordering.Application.Payments.PaymentStatus;

namespace Ordering.Infrastructure.Payments;

public class XsollaPaymentProvider : IPaymentProvider
{
    private readonly string _merchantId;
    private readonly string _apiKey;
    private readonly string _projectId;
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly string? _webhookSecret;

    public DomainPaymentMethod Provider => DomainPaymentMethod.Xsolla;

    public XsollaPaymentProvider(string merchantId, string apiKey, string projectId, string mode = "sandbox", string? webhookSecret = null)
    {
        _merchantId = merchantId;
        _apiKey = apiKey;
        _projectId = projectId;
        _baseUrl = mode.ToLowerInvariant() == "live"
            ? "https://api.xsolla.com"
            : "https://api.xsolla.com";
        _webhookSecret = webhookSecret;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("X-Secret-Id", _merchantId);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string currency,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            settings = new
            {
                currency = currency.ToUpperInvariant(),
                language = "en",
                return_url = "https://GameShop.local/order/complete"
            },
            purchase = new
            {
                checkout = new
                {
                    amount = (double)amount,
                    currency = currency.ToUpperInvariant()
                }
            },
            external_id = orderId.ToString("D"),
            custom_parameters = new
            {
                order_id = orderId.ToString("D")
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v3/project/{_projectId}/admin/payment/token")
        {
            Content = JsonContent.Create(requestBody)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Xsolla payment token creation failed: {content}");
        }

        using var doc = JsonDocument.Parse(content);
        var token = doc.RootElement.GetProperty("token").GetString();
        var checkoutUrl = $"https://secure.xsolla.com/paystation4/?token={token}";

        return new PaymentIntentResult(
            ExternalId: token ?? Guid.NewGuid().ToString(),
            CheckoutUrl: checkoutUrl,
            Status: DomainPaymentStatus.Pending
        );
    }

    public Task<WebhookResult> ParseWebhookAsync(Stream body, string? signature, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            throw new InvalidOperationException("Webhook secret not configured.");
        }

        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);
        var payload = reader.ReadToEnd();

        try
        {
            var json = JsonDocument.Parse(payload);
            var root = json.RootElement;

            var eventType = root.TryGetProperty("event", out var eventProp) 
                ? eventProp.GetString() 
                : root.TryGetProperty("notification_type", out var notifProp) 
                    ? notifProp.GetString() 
                    : null;

            if (eventType == "payment" || eventType == "payment_success" || eventType == "order_paid")
            {
                var orderIdStr = root.TryGetProperty("external_id", out var extId) 
                    ? extId.GetString() 
                    : root.TryGetProperty("custom_parameters", out var custom) && custom.TryGetProperty("order_id", out var orderIdProp)
                        ? orderIdProp.GetString()
                        : null;

                var orderId = !string.IsNullOrEmpty(orderIdStr) && Guid.TryParse(orderIdStr, out var parsed)
                    ? parsed
                    : Guid.NewGuid();

                var transactionId = root.TryGetProperty("transaction_id", out var transId) 
                    ? transId.GetString() 
                    : null;
                transactionId ??= Guid.NewGuid().ToString();

                return Task.FromResult(new WebhookResult(
                    OrderId: orderId,
                    PaymentId: Guid.NewGuid(),
                    EventId: transactionId,
                    Status: "succeeded"
                ));
            }

            if (eventType == "payment_declined" || eventType == "payment_canceled" || eventType == "refund")
            {
                var orderIdStr = root.TryGetProperty("external_id", out var extId)
                    ? extId.GetString()
                    : root.TryGetProperty("custom_parameters", out var custom) && custom.TryGetProperty("order_id", out var orderIdProp)
                        ? orderIdProp.GetString()
                        : null;

                var orderId = !string.IsNullOrEmpty(orderIdStr) && Guid.TryParse(orderIdStr, out var parsed)
                    ? parsed
                    : Guid.NewGuid();

                var transactionId = root.TryGetProperty("transaction_id", out var transId)
                    ? transId.GetString()
                    : null;
                transactionId ??= Guid.NewGuid().ToString();

                return Task.FromResult(new WebhookResult(
                    OrderId: orderId,
                    PaymentId: Guid.NewGuid(),
                    EventId: transactionId,
                    Status: "failed"
                ));
            }

            throw new InvalidOperationException($"Unhandled Xsolla event type: {eventType}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse Xsolla webhook: {ex.Message}");
        }
    }
}
