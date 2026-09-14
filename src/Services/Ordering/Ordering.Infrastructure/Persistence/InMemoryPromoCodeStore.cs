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
        Guid.Parse("d1a00000-0000-0000-0000-000000000150"),
        Guid.Parse("d1a00000-0000-0000-0000-000000000300"),
        Guid.Parse("d1a00000-0000-0000-0000-000000000450"),
        Guid.Parse("d1a00000-0000-0000-0000-000000000600"),
        Guid.Parse("d1a00000-0000-0000-0000-000000001200"),
        Guid.Parse("d1a00000-0000-0000-0000-000000002500")
    };

    private readonly ConcurrentDictionary<string, PromoCode> _codes = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryPromoCodeStore()
    {
        foreach (var promo in DefaultCodes())
        {
            _codes.TryAdd(promo.Code.Trim(), promo);
        }
    }

    public static IReadOnlyList<PromoCode> DefaultCodes()
    {
        var now = DateTimeOffset.UtcNow;

        return
        [
            new PromoCode("GAME10", DiscountType.Percent, 10m, "EUR", true, null, now.AddDays(365), 10_000, 0, new HashSet<Guid>()),
            new PromoCode("PREM20", DiscountType.Percent, 20m, "EUR", true, null, now.AddDays(90), 2_000, 0, new HashSet<Guid>(PremiumProductIds)),
            new PromoCode("SAVE5", DiscountType.FixedAmount, 5m, "EUR", true, null, now.AddDays(30), 500, 0, new HashSet<Guid>(DiamondProductIds)),
        ];
    }

    public Task<PromoCode?> GetAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult<PromoCode?>(null);
        }

        return Task.FromResult(_codes.TryGetValue(code.Trim(), out var promo) ? promo : null);
    }

    public Task<bool> TryConsumeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult(false);
        }

        var key = code.Trim();
        while (true)
        {
            if (!_codes.TryGetValue(key, out var promo))
            {
                return Task.FromResult(false);
            }

            if (promo.MaxUses > 0 && promo.UsedCount >= promo.MaxUses)
            {
                return Task.FromResult(false);
            }

            var updated = promo with { UsedCount = promo.UsedCount + 1 };
            if (_codes.TryUpdate(key, updated, promo))
            {
                return Task.FromResult(true);
            }
        }
    }
}
