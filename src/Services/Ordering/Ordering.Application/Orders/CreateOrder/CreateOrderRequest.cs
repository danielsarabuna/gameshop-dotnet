namespace Ordering.Application.Orders.CreateOrder;

public sealed record CreateOrderRequest(string BuyerId, IReadOnlyList<CreateOrderItem> Items);

public sealed record CreateOrderItem(
    Guid ProductId,
    string Title,
    decimal UnitPrice,
    int Quantity
);

