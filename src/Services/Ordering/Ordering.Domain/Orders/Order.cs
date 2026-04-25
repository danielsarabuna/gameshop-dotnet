namespace Ordering.Domain.Orders;

using Ordering.Domain.Payments;

public sealed class Order
{
    public Order(
        Guid id,
        string gameUserId,
        PaymentMethod paymentMethod,
        IReadOnlyList<OrderItem> items,
        string currency,
        decimal discountAmount,
        string? promoCode,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(gameUserId))
        {
            throw new ArgumentException("GameUserId is required.", nameof(gameUserId));
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("Order must contain at least one item.", nameof(items));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        if (discountAmount < 0)
        {
            throw new ArgumentException("Discount cannot be negative.", nameof(discountAmount));
        }

        var subtotal = items.Sum(item => item.LineTotal);
        if (discountAmount > subtotal)
        {
            throw new ArgumentException("Discount cannot exceed subtotal.", nameof(discountAmount));
        }

        Id = id;
        GameUserId = gameUserId;
        PaymentMethod = paymentMethod;
        Items = items;
        Currency = currency;
        DiscountAmount = discountAmount;
        PromoCode = promoCode;
        CreatedAtUtc = createdAtUtc;
        Status = OrderStatus.Pending;
    }

    public Guid Id { get; }
    public string GameUserId { get; }
    public PaymentMethod PaymentMethod { get; private set; }
    public IReadOnlyList<OrderItem> Items { get; }
    public string Currency { get; }
    public string? PromoCode { get; }
    public decimal DiscountAmount { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public OrderStatus Status { get; private set; }
    public string? PaymentId { get; private set; }
    public DateTimeOffset? PaidAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    public decimal Subtotal => Items.Sum(item => item.LineTotal);

    public decimal Total => Math.Max(0, Subtotal - DiscountAmount);

    public void SetPaymentMethod(PaymentMethod method)
    {
        if (method == PaymentMethod.Unspecified)
        {
            throw new ArgumentException("PaymentMethod is required.", nameof(method));
        }

        if (PaymentMethod != PaymentMethod.Unspecified && PaymentMethod != method)
        {
            throw new ArgumentException("Payment method mismatch.", nameof(method));
        }

        PaymentMethod = method;
    }

    public void MarkPaid(string paymentId, DateTimeOffset paidAtUtc)
    {
        if (Status == OrderStatus.Paid)
        {
            return;
        }

        Status = OrderStatus.Paid;
        PaymentId = paymentId;
        PaidAtUtc = paidAtUtc;
        FailureReason = null;
    }

    public void MarkFailed(string reason)
    {
        if (Status == OrderStatus.Paid)
        {
            return;
        }

        Status = OrderStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "failed" : reason;
    }
}
