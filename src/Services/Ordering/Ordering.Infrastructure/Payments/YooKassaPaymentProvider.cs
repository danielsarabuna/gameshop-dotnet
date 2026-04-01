using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;
using DomainPaymentStatus = Ordering.Application.Payments.PaymentStatus;

namespace Ordering.Infrastructure.Payments;

public class YooKassaPaymentProvider : IPaymentProvider
{
    private readonly string _shopId;
    private readonly string _secretKey;
    private readonly HttpClient _httpClient;
    private readonly string? _webhookSecret;
    private const string BaseUrl = "https://api.yookassa.ru/v3";

    public DomainPaymentMethod Provider => DomainPaymentMethod.YooKassa;

    public YooKassaPaymentProvider(string shopId, string secretKey, string? webhookSecret = null)
    {
        _shopId = shopId;
        _secretKey = secretKey;
        _webhookSecret = webhookSecret;
        _httpClient = new HttpClient();
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{shopId}:{secretKey}"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string currency,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            amount = new
            {
                value = amount.ToString("F2"),
                currency = currency.ToUpperInvariant() switch
                {
                    "EUR" => "EUR",
                    "RUB" => "RUB",
                    "USD" => "USD",
                    _ => "EUR"
                }
            },
            payment_method_data = new { type = " YooKassa" },
            confirmation = new { type = "redirect", return_url = "https://GameShop.local/order/complete" },
            description = $"Order {orderId:D}",
            metadata = new { order_id = orderId.ToString("D") }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/payments")
        {
            Content = JsonContent.Create(requestBody)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"YooKassa payment creation failed: {content}");
        }

        using var doc = JsonDocument.Parse(content);
        var paymentId = doc.RootElement.GetProperty("id").GetString()!;
        var confirmationUrl = doc.RootElement.GetProperty("confirmation").GetProperty("confirmation_url").GetString()!;

        return new PaymentIntentResult(
            ExternalId: paymentId,
            CheckoutUrl: confirmationUrl,
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

            var eventType = root.GetProperty("event").GetString();

            if (eventType == "payment.succeeded" || eventType == "payment.waiting_for_capture")
            {
                var payment = root.GetProperty("object");
                var paymentId = payment.GetProperty("id").GetString();
                var status = payment.GetProperty("status").GetString();

                Guid orderId;
                if (payment.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("order_id", out var orderIdElement))
                {
                    orderId = Guid.Parse(orderIdElement.GetString()!);
                }
                else
                {
                    orderId = Guid.NewGuid();
                }

                var webhookStatus = status switch
                {
                    "succeeded" => "succeeded",
                    "waiting_for_capture" => "pending",
                    _ => "pending"
                };

                return Task.FromResult(new WebhookResult(
                    OrderId: orderId,
                    PaymentId: Guid.NewGuid(),
                    EventId: paymentId ?? Guid.NewGuid().ToString(),
                    Status: webhookStatus
                ));
            }

            if (eventType == "payment.canceled" || eventType == "payment.failed")
            {
                var payment = root.GetProperty("object");
                var paymentId = payment.GetProperty("id").GetString();

                Guid orderId;
                if (payment.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("order_id", out var orderIdElement))
                {
                    orderId = Guid.Parse(orderIdElement.GetString()!);
                }
                else
                {
                    orderId = Guid.NewGuid();
                }

                return Task.FromResult(new WebhookResult(
                    OrderId: orderId,
                    PaymentId: Guid.NewGuid(),
                    EventId: paymentId ?? Guid.NewGuid().ToString(),
                    Status: "failed"
                ));
            }

            throw new InvalidOperationException($"Unhandled event type: {eventType}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse YooKassa webhook: {ex.Message}");
        }
    }
}