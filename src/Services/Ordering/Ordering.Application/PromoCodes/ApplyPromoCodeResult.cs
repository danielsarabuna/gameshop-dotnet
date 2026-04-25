namespace Ordering.Application.PromoCodes;

public sealed record ApplyPromoCodeResult(
    bool IsValid,
    string? Error,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal Total,
    string Currency
);

