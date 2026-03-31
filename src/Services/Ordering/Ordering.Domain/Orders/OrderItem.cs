namespace Ordering.Domain.Orders;

using Ordering.Domain.Products;

public sealed record OrderItem(
    Guid ProductId,
    string Title,
    ProductType Type,
    decimal UnitPrice,
    int Quantity,
    IReadOnlyDictionary<string, string> Metadata
)
{
    public decimal LineTotal => UnitPrice * Quantity;
}
