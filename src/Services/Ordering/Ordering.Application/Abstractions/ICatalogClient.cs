using Ordering.Domain.Products;

namespace Ordering.Application.Abstractions;

public interface ICatalogClient
{
    Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken cancellationToken);

    Task<CatalogProduct?> GetProductAsync(Guid id, CatalogScope scope, CancellationToken cancellationToken)
        => GetProductAsync(id, cancellationToken);
}

public sealed record CatalogScope(string Region, string Store, string GameVersion)
{
    public static readonly CatalogScope Default = new("global", "global", "global");
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
