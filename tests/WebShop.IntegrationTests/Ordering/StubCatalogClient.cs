using System.Collections.Concurrent;
using Ordering.Application.Abstractions;
using Ordering.Domain.Products;

namespace WebShop.IntegrationTests.OrderingApi;

public sealed class StubCatalogClient : ICatalogClient
{
    private readonly ConcurrentDictionary<Guid, CatalogProduct> _items = new();

    public StubCatalogClient Add(CatalogProduct product)
    {
        _items[product.Id] = product;
        return this;
    }

    public Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken cancellationToken)
    {
        _items.TryGetValue(id, out var product);
        return Task.FromResult<CatalogProduct?>(product);
    }
}
