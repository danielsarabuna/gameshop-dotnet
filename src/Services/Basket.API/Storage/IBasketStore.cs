namespace Basket.API.Storage;

public interface IBasketStore
{
    Basket? Get(string userId);
    void Upsert(Basket basket);
    void Delete(string userId);
}

public sealed record Basket(string UserId, IReadOnlyList<BasketItem> Items);

public sealed record BasketItem(
    Guid ProductId,
    string Title,
    decimal UnitPrice,
    int Quantity
);

public sealed record UpdateBasketRequest(IReadOnlyList<BasketItem> Items);

