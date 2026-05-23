using EventBus;

namespace IntegrationEvents;

public sealed record BasketCheckout(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string UserId,
    IReadOnlyList<BasketCheckoutItem> Items,
    decimal Total,
    string Currency
) : IntegrationEvent(Id, OccurredAtUtc);

public sealed record BasketCheckoutItem(
    Guid ProductId,
    string Title,
    decimal UnitPrice,
    int Quantity
);
