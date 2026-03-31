namespace Ordering.Application.PromoCodes;

public sealed record PromoCode(
    string Code,
    DiscountType Type,
    decimal Value,
    string Currency,
    bool IsActive,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    int MaxUses,
    int UsedCount,
    IReadOnlySet<Guid> ProductIds
);

