using EventBus;

namespace IntegrationEvents;

public sealed record OrderCompleted(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid OrderId,
    string GameUserId,
    decimal Total,
    string Currency,
    string PaymentId,
    DateTimeOffset PaidAtUtc
) : IntegrationEvent(Id, OccurredAtUtc);
