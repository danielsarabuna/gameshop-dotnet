using Ordering.Domain.Products;

namespace Ordering.Application.Abstractions;

public interface ICatalogClient
{
    Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record CatalogProduct(
    Guid Id,
    string Title,
    string Description,
    ProductType Type,
    decimal Price,
    string Currency,
    bool IsActive,
    IReadOnlyDictionary<string, string> Metadata
);

