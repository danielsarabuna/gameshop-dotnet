using Catalog.Grpc;
using Grpc.Core;

namespace Basket.API.Storage;

/// <summary>Authoritative product data for basket validation (price/currency come from the catalog).</summary>
public interface IBasketCatalogClient
{
    Task<BasketProduct?> GetProductAsync(Guid productId, CancellationToken cancellationToken);
}

public sealed record BasketProduct(Guid Id, string Title, decimal Price, string Currency, bool IsActive);

/// <summary>gRPC adapter around CatalogInternal.</summary>
public sealed class GrpcBasketCatalogClient(CatalogInternal.CatalogInternalClient client) : IBasketCatalogClient
{
    public async Task<BasketProduct?> GetProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            var product = await client.GetProductAsync(
                new GetProductRequest { Id = productId.ToString("D") },
                cancellationToken: cancellationToken);

            return new BasketProduct(
                Guid.Parse(product.Id),
                product.Title,
                decimal.Parse(product.Price, System.Globalization.CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(product.Currency) ? "USD" : product.Currency,
                product.IsActive);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }
}
