namespace Ordering.Application.PromoCodes;

public sealed record ApplyPromoCodeRequest(
    string Code,
    IReadOnlyList<PromoCartLine> Items
);

public sealed record PromoCartLine(
    Guid ProductId,
    int Quantity
);

