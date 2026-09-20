using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;
using DomainPaymentStatus = Ordering.Application.Payments.PaymentStatus;

namespace Ordering.Infrastructure.Payments;

public class CorvusPayPaymentProvider : IPaymentProvider
{
    private readonly string _storeId;
    private readonly string _secretKey;
    private readonly string _apiUrl;
    private readonly HttpClient _httpClient;
    private readonly string? _webhookSecret;

    public DomainPaymentMethod Provider => DomainPaymentMethod.CorvusPay;

    public CorvusPayPaymentProvider(string storeId, string secretKey, string apiUrl = "https://corvuspay.com/payment", string? webhookSecret = null)
    {
        _storeId = storeId;
        _secretKey = secretKey;
        _apiUrl = apiUrl;
        _webhookSecret = webhookSecret;
        _httpClient = new HttpClient();
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string currency,
        Guid orderId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            store_id = _storeId,
            order_number = orderId.ToString("D"),
            amount = amount.ToString("F2", CultureInfo.InvariantCulture),
            currency = currency.ToUpperInvariant(),
            description = $"Order {orderId:D}",
            custom = orderId.ToString("D")
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiUrl}/transaction")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_storeId}:{_secretKey}")));

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"CorvusPay payment creation failed: {content}");
        }

        using var doc = JsonDocument.Parse(content);

        string? transactionId;
        string? checkoutUrl;

        if (doc.RootElement.TryGetProperty("transaction", out var transaction))
        {
            transactionId = transaction.GetProperty("id").GetString();
            checkoutUrl = transaction.GetProperty("checkout_url").GetString();
        }
        else
        {
            transactionId = doc.RootElement.GetProperty("id").GetString();
            checkoutUrl = $"{_apiUrl}/checkout?transaction={transactionId}";
        }

        if (string.IsNullOrEmpty(checkoutUrl))
        {
            throw new InvalidOperationException("No checkout URL from CorvusPay");
        }

        return new PaymentIntentResult(
            ExternalId: transactionId ?? Guid.NewGuid().ToString(),
            CheckoutUrl: checkoutUrl,
            Status: DomainPaymentStatus.Pending
        );
    }

    public Task<WebhookResult?> ParseWebhookAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        // Not a production provider (see docs/payments): webhook verification is intentionally
        // unimplemented so it can never be trusted by accident.
        throw new NotImplementedException("CorvusPay webhook verification is not implemented.");
    }
}
