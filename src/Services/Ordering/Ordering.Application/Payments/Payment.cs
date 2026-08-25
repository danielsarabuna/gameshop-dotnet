using Ordering.Domain.Payments;

namespace Ordering.Application.Payments;

public sealed record Payment(
    Guid Id,
    Guid OrderId,
    PaymentMethod Provider,
    PaymentStatus Status,
    string? ExternalId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? CheckoutUrl = null
);
