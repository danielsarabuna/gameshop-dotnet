using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.CreateOrder;

public sealed record CreateOrderResult(
    Guid OrderId,
    OrderStatus Status,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    string Currency
);
