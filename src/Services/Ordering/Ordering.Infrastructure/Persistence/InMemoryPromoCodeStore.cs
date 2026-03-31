using System.Collections.Concurrent;
using Ordering.Application.Abstractions;
using Ordering.Application.PromoCodes;

namespace Ordering.Infrastructure.Persistence;

public sealed class InMemoryPromoCodeStore : IPromoCodeStore
{
    private static readonly IReadOnlySet<Guid> PremiumProductIds = new HashSet<Guid>
    {
        Guid.Parse("9aa00000-0000-0000-0000-000000000001"),
        Guid.Parse("9aa00000-0000-0000-0000-000000000003"),
        Guid.Parse("9aa00000-0000-0000-0000-000000000012")
    };

    private static readonly IReadOnlySet<Guid> DiamondProductIds = new HashSet<Guid>
    {
        Guid.Parse("d1a00000-0000-0000-0000-000000000060"),
        Guid.Parse("d1a00000-0000-0000-0000-000000000120"),
        Guid.Parse("d1a00000-0000-0000-0000-000000000350"),
        Guid.Parse("d1a00000-0000-0000-0000-000000000800"),
        Guid.Parse("d1a00000-0000-0000-0000-000000002000"),
        Guid.Parse("d1a00000-0000-0000-0000-000000004500"),
        Guid.Parse("d1a00000-0000-0000-0000-000000009000"),
        Guid.Parse("d1a00000-0000-0000-0000-000000015000"),
        Guid.Parse("d1a00000-0000-0000-0000-000000025000")
    };

    private readonly ConcurrentDictionary<string, PromoCode> _codes = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryPromoCodeStore()
    {
        var now = DateTimeOffset.UtcNow;

        Seed(new PromoCode(
            Code: "LOVE10",
            Type: DiscountType.Percent,
            Value: 10m,
            Currency: "EUR",
            IsActive: true,
            StartsAtUtc: null,
            ExpiresAtUtc: now.AddDays(365),
            MaxUses: 10_000,
            UsedCount: 0,
            ProductIds: new HashSet<Guid>()
        ));

        Seed(new PromoCode(
            Code: "PREM20",
            Type: DiscountType.Percent,
            Value: 20m,
            Currency: "EUR",
            IsActive: true,
            StartsAtUtc: null,
            ExpiresAtUtc: now.AddDays(90),
            MaxUses: 2_000,
            UsedCount: 0,
            ProductIds: PremiumProductIds
        ));

        Seed(new PromoCode(
            Code: "SAVE5",
            Type: DiscountType.FixedAmount,
            Value: 5m,
            Currency: "EUR",
            IsActive: true,
            StartsAtUtc: null,
            ExpiresAtUtc: now.AddDays(30),
            MaxUses: 500,
            UsedCount: 0,
            ProductIds: DiamondProductIds
        ));
    }

    public PromoCode? Get(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return _codes.TryGetValue(code.Trim(), out var promo) ? promo : null;
    }

    public bool TryConsume(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var key = code.Trim();
        while (true)
        {
            if (!_codes.TryGetValue(key, out var promo))
            {
                return false;
            }

            if (promo.MaxUses > 0 && promo.UsedCount >= promo.MaxUses)
            {
                return false;
            }

            var updated = promo with { UsedCount = promo.UsedCount + 1 };
            if (_codes.TryUpdate(key, updated, promo))
            {
                return true;
            }
        }
    }

    private void Seed(PromoCode promoCode)
    {
        _codes.TryAdd(promoCode.Code.Trim(), promoCode);
    }
}

