using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;
using DomainPaymentStatus = Ordering.Application.Payments.PaymentStatus;

namespace Ordering.Infrastructure.Payments;

public class XsollaPaymentProvider : IPaymentProvider
{
    private const string SignaturePrefix = "Signature ";
    private readonly string _merchantId;
    private readonly string _apiKey;
    private readonly string _projectId;
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly string? _webhookSecret;
    private readonly string _returnUrl;

    public DomainPaymentMethod Provider => DomainPaymentMethod.Xsolla;

    public XsollaPaymentProvider(string merchantId, string apiKey, string projectId, string mode = "sandbox", string? webhookSecret = null, string? returnUrl = null)
    {
        _merchantId = merchantId;
        _apiKey = apiKey;
        _projectId = projectId;
        _baseUrl = string.Equals(mode, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://api.xsolla.com"
            : "https://api.xsolla.com";
        _webhookSecret = webhookSecret;
        _returnUrl = returnUrl ?? "https://example.invalid/order/complete";
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("X-Secret-Id", _merchantId);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
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
            settings = new
            {
                currency = currency.ToUpperInvariant(),
                language = "en",
                return_url = $"{_returnUrl}?order_id={orderId:D}"
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
        request.Headers.Add("Idempotency-Key", idempotencyKey);

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

    public Task<WebhookResult?> ParseWebhookAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            throw new InvalidOperationException("Xsolla webhook secret is not configured.");
        }

        // Documented Xsolla scheme: Authorization: "Signature <sha1_hex(md5_hex(body + secret))>".
        if (!envelope.Headers.TryGetValue("Authorization", out var authorization)
            || !VerifySignature(envelope.Body, _webhookSecret, authorization))
        {
            throw new CryptographicException("Xsolla webhook signature verification failed.");
        }

        try
        {
            var root = JsonDocument.Parse(envelope.Body).RootElement;

            var notificationType = root.TryGetProperty("notification_type", out var notifProp)
                ? notifProp.GetString()
                : null;

            switch (notificationType)
            {
                case "payment":
                    return Task.FromResult<WebhookResult?>(BuildResult(root, "succeeded"));
                case "refund":
                case "canceled":
                    return Task.FromResult<WebhookResult?>(BuildResult(root, "failed"));
                default:
                    // user_validation / user_search etc. — acknowledged, no side effects.
                    return Task.FromResult<WebhookResult?>(null);
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException($"Failed to parse Xsolla webhook: {ex.Message}");
        }
    }

    private static WebhookResult BuildResult(JsonElement root, string status)
    {
        var orderIdStr =
            root.TryGetProperty("external_id", out var extId) ? extId.GetString()
            : root.TryGetProperty("custom_parameters", out var custom) && custom.TryGetProperty("order_id", out var orderIdProp)
                ? orderIdProp.GetString()
                : null;

        if (string.IsNullOrEmpty(orderIdStr) || !Guid.TryParse(orderIdStr, out var orderId))
        {
            throw new InvalidOperationException("Xsolla webhook is missing a valid order reference.");
        }

        var transactionId = root.TryGetProperty("transaction_id", out var transId)
            ? transId.GetString()
            : null;
        transactionId ??= Guid.NewGuid().ToString();

        decimal? amount = null;
        string? currency = null;
        if (root.TryGetProperty("purchase", out var purchase)
            && purchase.TryGetProperty("checkout", out var checkout)
            && checkout.TryGetProperty("amount", out var amountEl)
            && amountEl.TryGetDecimal(out var parsedAmount))
        {
            amount = parsedAmount;
            currency = checkout.TryGetProperty("currency", out var curEl) ? curEl.GetString() : null;
        }

        return new WebhookResult(
            OrderId: orderId,
            PaymentId: Guid.NewGuid(),
            EventId: transactionId,
            Status: status,
            Amount: amount,
            Currency: currency);
    }

    public static bool VerifySignature(string body, string secret, string? authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader)
            || !authorizationHeader.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var provided = authorizationHeader[SignaturePrefix.Length..].Trim();
#pragma warning disable CA5350, CA5351 // Required by the documented Xsolla webhook signature protocol.
        var md5Hex = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(body + secret)));
        var expected = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(md5Hex)));
#pragma warning restore CA5350, CA5351

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));
    }
}
