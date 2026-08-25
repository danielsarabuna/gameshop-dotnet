using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;
using DomainPaymentStatus = Ordering.Application.Payments.PaymentStatus;

namespace Ordering.Infrastructure.Payments;

public class YooKassaPaymentProvider : IPaymentProvider
{
    private const string BaseUrl = "https://api.yookassa.ru/v3";
    private readonly string _shopId;
    private readonly string _secretKey;
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<string> _trustedIps;
    private readonly string _returnUrl;

    public DomainPaymentMethod Provider => DomainPaymentMethod.YooKassa;

    public YooKassaPaymentProvider(
        string shopId,
        string secretKey,
        IEnumerable<string>? trustedIps = null,
        string? returnUrl = null)
    {
        _shopId = shopId;
        _secretKey = secretKey;
        _trustedIps = trustedIps?.ToArray() ?? [];
        // Where the player's browser lands after paying at YooKassa.
        _returnUrl = returnUrl ?? "https://GameShop.local/order/complete";
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
        // No payment_method_data: the player picks the method on YooKassa's side.
        // (A hardcoded type here caused 400s from the API.)
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
            capture = true,
            confirmation = new { type = "redirect", return_url = $"{_returnUrl}?order_id={orderId:D}" },
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

    public async Task<WebhookResult?> ParseWebhookAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        // Layer 1 — source allowlist (documented YooKassa notification networks + optional overrides).
        if (!YooKassaWebhookSource.IsAllowed(envelope.RemoteIp, _trustedIps))
        {
            throw new UnauthorizedAccessException($"Webhook source '{envelope.RemoteIp}' is not in the YooKassa allowlist.");
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(envelope.Body).RootElement;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Malformed YooKassa webhook payload: {ex.Message}");
        }

        var eventType = root.TryGetProperty("event", out var eventProp) ? eventProp.GetString() : null;
        if (eventType != "payment.succeeded")
        {
            // canceled / waiting_for_capture etc. — acknowledged; terminal failure arrives as its own event.
            return null;
        }

        var notification = root.GetProperty("object");
        var paymentId = notification.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("YooKassa webhook is missing object.id.");

        // Layer 2 — authoritative confirmation: re-read the payment object from the YooKassa API.
        // A spoofed notification can never fabricate what the real API returns.
        using var confirmationResponse = await _httpClient.GetAsync($"{BaseUrl}/payments/{paymentId}", cancellationToken);
        if (!confirmationResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"YooKassa confirmation request failed ({(int)confirmationResponse.StatusCode}).");
        }

        var payment = await confirmationResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var status = payment.GetProperty("status").GetString();
        var paid = payment.TryGetProperty("paid", out var paidEl) && paidEl.ValueKind == JsonValueKind.True;

        if (!string.Equals(status, "succeeded", StringComparison.Ordinal) || !paid)
        {
            throw new InvalidOperationException($"YooKassa API reports payment '{paymentId}' as '{status}' (paid={paid}).");
        }

        var orderIdStr = payment.TryGetProperty("metadata", out var metadata)
            && metadata.TryGetProperty("order_id", out var orderIdEl)
            ? orderIdEl.GetString()
            : null;

        if (string.IsNullOrEmpty(orderIdStr) || !Guid.TryParse(orderIdStr, out var orderId))
        {
            throw new InvalidOperationException("YooKassa payment is missing metadata.order_id.");
        }

        var amountValue = decimal.Parse(
            payment.GetProperty("amount").GetProperty("value").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture);

        return new WebhookResult(
            OrderId: orderId,
            PaymentId: Guid.NewGuid(),
            EventId: paymentId,
            Status: "succeeded",
            Amount: amountValue,
            Currency: payment.GetProperty("amount").GetProperty("currency").GetString());
    }
}

/// <summary>IPv4 CIDR/exact matching against the documented YooKassa notification source networks.</summary>
public static class YooKassaWebhookSource
{
    // https://yookassa.ru/developers/using-api/webhooks#verify — official notification source IPs.
    internal static readonly string[] DefaultRanges =
    [
        "185.71.76.0/27",
        "185.71.77.0/27",
        "77.75.153.0/25",
        "77.75.156.11",
        "77.75.156.35",
        "77.75.154.128/25",
    ];

    public static bool IsAllowed(string? remoteIp, IEnumerable<string>? extraRules = null)
    {
        if (string.IsNullOrWhiteSpace(remoteIp) || !IPAddress.TryParse(remoteIp, out var ip))
        {
            return false;
        }

        foreach (var rule in DefaultRanges.Concat(extraRules ?? []).Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            if (Matches(ip, rule.Trim()))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool Matches(IPAddress ip, string rule)
    {
        var parts = rule.Split('/');
        if (!IPAddress.TryParse(parts[0], out var network))
        {
            return false;
        }

        if (network.AddressFamily != AddressFamily.InterNetwork || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            // IPv6 rules are matched exactly only (the /32 range is intentionally unsupported here).
            return network.AddressFamily == ip.AddressFamily && network.Equals(ip);
        }

        var prefixLength = parts.Length == 2 ? int.Parse(parts[1]) : 32;
        if (prefixLength is < 0 or > 32)
        {
            return false;
        }

        uint mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        return (ToUInt32(ip) & mask) == (ToUInt32(network) & mask);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }
}
