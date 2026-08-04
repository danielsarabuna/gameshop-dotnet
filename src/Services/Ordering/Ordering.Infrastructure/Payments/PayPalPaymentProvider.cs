using System.Globalization;
using System.Net.Http.Json;
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

    public PayPalPaymentProvider(string clientId, string clientSecret, string mode = "sandbox", string? webhookSecret = null)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _baseUrl = string.Equals(mode, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
        _httpClient = new HttpClient();
        _webhookSecret = webhookSecret;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string currency,
        Guid orderId,
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

    public Task<WebhookResult?> ParseWebhookAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        // Not a production provider (see docs/payments): webhook verification is intentionally
        // unimplemented so it can never be trusted by accident.
        throw new NotImplementedException("PayPal webhook verification is not implemented.");
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
