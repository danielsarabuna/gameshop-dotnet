namespace Catalog.API.Storage;

public sealed class InMemoryCatalogStore : ICatalogStore
{
    public IReadOnlyList<CatalogItem> GetAll() => CatalogSeedData.Items;

    public CatalogItem? GetById(Guid id) => CatalogSeedData.Items.FirstOrDefault(item => item.Id == id);
}
