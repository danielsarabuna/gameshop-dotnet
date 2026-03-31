using Ordering.Application.PromoCodes;

namespace Ordering.Application.Abstractions;

public interface IPromoCodeStore
{
    PromoCode? Get(string code);
    bool TryConsume(string code);
}

