using System.Buffers;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;
using DomainPaymentStatus = Ordering.Application.Payments.PaymentStatus;

namespace Ordering.Infrastructure.Payments;

public class PayPalPaymentProvider : IPaymentProvider
{
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly string? _webhookSecret;
    private string? _accessToken;

    public DomainPaymentMethod Provider => DomainPaymentMethod.PayPal;

    public PayPalPaymentProvider(string clientId, string clientSecret, string mode = "sandbox", string? webhookSecret = null, HttpClient? httpClient = null)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _baseUrl = string.Equals(mode, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
        _httpClient = httpClient ?? new HttpClient();
        _webhookSecret = webhookSecret;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string currency,
        Guid orderId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await EnsureAccessTokenAsync(cancellationToken);

        var orderRequest = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = orderId.ToString("D"),
                    custom_id = orderId.ToString("D"),
                    amount = new
                    {
                        currency_code = currency.ToUpperInvariant(),
                        value = amount.ToString("F2", CultureInfo.InvariantCulture)
                    }
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/checkout/orders")
        {
            Content = JsonContent.Create(orderRequest)
        };
        request.Headers.Add("PayPal-Request-Id", idempotencyKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PayPal order creation failed: {content}");
        }

        var doc = JsonDocument.Parse(content);
        var payPalOrderId = doc.RootElement.GetProperty("id").GetString()!;
        var approveLink = doc.RootElement.GetProperty("links").EnumerateArray()
            .FirstOrDefault(l => l.GetProperty("rel").GetString() == "approve")
            .GetProperty("href")
            .GetString()!;

        return new PaymentIntentResult(
            ExternalId: payPalOrderId,
            CheckoutUrl: approveLink,
            Status: DomainPaymentStatus.Pending
        );
    }

    public async Task<WebhookResult?> ParseWebhookAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_webhookSecret))
            throw new InvalidOperationException("PayPal webhook ID is not configured.");

        string Header(string name) => envelope.Headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new UnauthorizedAccessException($"Missing {name} header.");

        await EnsureAccessTokenAsync(cancellationToken);
        var verification = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/notifications/verify-webhook-signature")
        {
            Content = CreateVerificationContent(envelope.Body, Header)
        };
        verification.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        using var response = await _httpClient.SendAsync(verification, cancellationToken);
        var verified = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (!response.IsSuccessStatusCode || !verified.TryGetProperty("verification_status", out var status) || !string.Equals(status.GetString(), "SUCCESS", StringComparison.Ordinal))
            throw new CryptographicException("PayPal webhook signature verification failed.");

        using var payload = JsonDocument.Parse(envelope.Body);
        var root = payload.RootElement;
        if (!string.Equals(root.GetProperty("event_type").GetString(), "PAYMENT.CAPTURE.COMPLETED", StringComparison.Ordinal))
            return null;
        var resource = root.GetProperty("resource");
        if (!Guid.TryParse(resource.GetProperty("custom_id").GetString(), out var orderId))
            throw new InvalidOperationException("PayPal webhook is missing resource.custom_id order reference.");
        var amount = resource.GetProperty("amount");
        return new WebhookResult(orderId, Guid.NewGuid(), root.GetProperty("id").GetString()!, "succeeded", decimal.Parse(amount.GetProperty("value").GetString()!, CultureInfo.InvariantCulture), amount.GetProperty("currency_code").GetString());
    }

    private HttpContent CreateVerificationContent(string rawBody, Func<string, string> header)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("auth_algo", header("PayPal-Auth-Algo"));
            writer.WriteString("cert_url", header("PayPal-Cert-Url"));
            writer.WriteString("transmission_id", header("PayPal-Transmission-Id"));
            writer.WriteString("transmission_sig", header("PayPal-Transmission-Sig"));
            writer.WriteString("transmission_time", header("PayPal-Transmission-Time"));
            writer.WriteString("webhook_id", _webhookSecret);
            writer.WritePropertyName("webhook_event");
            writer.WriteRawValue(rawBody, skipInputValidation: true);
            writer.WriteEndObject();
        }

        return new ByteArrayContent(buffer.WrittenSpan.ToArray())
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") }
        };
    }

    private async Task EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken))
            return;

        var credentials = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/oauth2/token")
        {
            Headers = { { "Authorization", $"Basic {credentials}" } },
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            })
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PayPal token fetch failed: {content}");
        }

        var doc = JsonDocument.Parse(content);
        _accessToken = doc.RootElement.GetProperty("access_token").GetString();
    }
}
