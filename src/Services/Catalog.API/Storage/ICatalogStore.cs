namespace Catalog.API.Storage;

public interface ICatalogStore
{
    IReadOnlyList<CatalogItem> GetAll();
    CatalogItem? GetById(Guid id);
}

public sealed record CatalogItem(
    Guid Id,
    string Title,
    string Description,
    decimal Price,
    string? ImageUrl
);

