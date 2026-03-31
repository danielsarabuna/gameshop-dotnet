using System.Collections.Concurrent;

namespace Basket.API.Storage;

public sealed class InMemoryBasketStore : IBasketStore
{
    private readonly ConcurrentDictionary<string, Basket> _baskets = new(StringComparer.Ordinal);

    public Basket? Get(string userId)
    {
        return _baskets.TryGetValue(userId, out var basket) ? basket : null;
    }

    public void Upsert(Basket basket)
    {
        _baskets.AddOrUpdate(basket.UserId, basket, (_, _) => basket);
    }

    public void Delete(string userId)
    {
        _baskets.TryRemove(userId, out _);
    }
}
