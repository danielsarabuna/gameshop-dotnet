using Ordering.Domain.Payments;

namespace Ordering.Application.Orders.CreateOrder;

public sealed record CreateOrderRequest(
    string GameUserId,
    PaymentMethod PaymentMethod,
    IReadOnlyList<CreateOrderLine> Items,
    string? PromoCode
);

public sealed record CreateOrderLine(
    Guid ProductId,
    int Quantity
);

