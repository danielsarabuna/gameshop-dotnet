using Ordering.Domain.Payments;

namespace Ordering.Application.Payments;

public interface IPaymentProviderAccessor
{
    IPaymentProvider? GetProvider(PaymentMethod method);
    IReadOnlyCollection<PaymentMethod> GetAvailableMethods();
}

/// <summary>Everything a provider may need to authenticate and parse an incoming webhook.</summary>
public sealed record WebhookEnvelope(
    string Body,
    IReadOnlyDictionary<string, string> Headers,
    string? RemoteIp);

public interface IPaymentProvider
{
    PaymentMethod Provider { get; }
    Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies webhook authenticity (cryptographic signature, or source IP allowlist plus
    /// provider-API confirmation) and extracts the payload.
    /// MUST throw when authenticity cannot be proven — the API endpoint fails closed.
    /// Returns null for authentic-but-non-terminal events (the caller acknowledges them with 200).
    /// </summary>
    Task<WebhookResult?> ParseWebhookAsync(WebhookEnvelope envelope, CancellationToken cancellationToken);
}

public record PaymentIntentResult(
    string ExternalId,
    string CheckoutUrl,
    PaymentStatus Status
);

/// <param name="Amount">Reported charged amount; when present the handler validates it against the order total.</param>
/// <param name="Currency">Reported charge currency (validated against the order currency together with Amount).</param>
public record WebhookResult(
    Guid OrderId,
    Guid PaymentId,
    string EventId,
    string Status,
    decimal? Amount = null,
    string? Currency = null
);
