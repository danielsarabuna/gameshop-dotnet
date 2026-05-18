using Microsoft.Extensions.Configuration;
using Ordering.Domain.Payments;

namespace Ordering.Application.Payments;

public interface IPaymentProviderAccessor
{
    IPaymentProvider? GetProvider(PaymentMethod method);
}

public interface IPaymentProvider
{
    PaymentMethod Provider { get; }
    Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId, CancellationToken cancellationToken);
    Task<WebhookResult> ParseWebhookAsync(Stream body, string? signature, CancellationToken cancellationToken);
}

public record PaymentIntentResult(
    string ExternalId,
    string CheckoutUrl,
    PaymentStatus Status
);

public record WebhookResult(
    Guid OrderId,
    Guid PaymentId,
    string EventId,
    string Status
);