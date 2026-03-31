namespace Ordering.Domain.Orders;

public sealed record OrderItem(
    Guid ProductId,
    string Title,
    decimal UnitPrice,
    int Quantity
)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

