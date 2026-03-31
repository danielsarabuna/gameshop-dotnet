namespace Catalog.API.Storage;

public interface ICatalogStore
{
    IReadOnlyList<CatalogItem> GetAll();
    CatalogItem? GetById(Guid id);
}

public enum CatalogProductType
{
    Currency,
    Item,
    Subscription,
    Bundle
}

public sealed record CatalogItem(
    Guid Id,
    string Title,
    string Description,
    CatalogProductType Type,
    decimal Price,
    string Currency,
    bool IsActive,
    IReadOnlyDictionary<string, string> Metadata,
    string? ImageUrl
);
