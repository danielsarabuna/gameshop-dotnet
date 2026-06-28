using Ordering.Application.PromoCodes;

namespace Ordering.Application.Abstractions;

public interface IPromoCodeStore
{
    Task<PromoCode?> GetAsync(string code, CancellationToken cancellationToken);

    /// <summary>Atomically consumes one use of the code; false when unknown, inactive or exhausted.</summary>
    Task<bool> TryConsumeAsync(string code, CancellationToken cancellationToken);
}
