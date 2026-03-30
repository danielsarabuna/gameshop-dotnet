namespace Ordering.Domain.Orders;

public sealed class Order
{
    public Order(Guid id, string buyerId, IReadOnlyList<OrderItem> items, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ArgumentException("BuyerId is required.", nameof(buyerId));
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("Order must contain at least one item.", nameof(items));
        }

        Id = id;
        BuyerId = buyerId;
        Items = items;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }
    public string BuyerId { get; }
    public IReadOnlyList<OrderItem> Items { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public decimal Total => Items.Sum(item => item.LineTotal);
}

