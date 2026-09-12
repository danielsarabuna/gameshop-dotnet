using System.Globalization;
using Catalog.Grpc;
using Grpc.Core;
using Ordering.Application.Abstractions;
using AppCatalogProduct = Ordering.Application.Abstractions.CatalogProduct;
using AppProductType = Ordering.Domain.Products.ProductType;

namespace Ordering.Infrastructure.Integrations;

public sealed class GrpcCatalogClient : ICatalogClient
{
    private readonly CatalogInternal.CatalogInternalClient _client;

    public GrpcCatalogClient(CatalogInternal.CatalogInternalClient client)
    {
        _client = client;
    }

    public async Task<AppCatalogProduct?> GetProductAsync(Guid id, CancellationToken cancellationToken)
        => await GetProductAsync(id, CatalogScope.Default, cancellationToken);

    public async Task<AppCatalogProduct?> GetProductAsync(Guid id, CatalogScope scope, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetProductAsync(
                new GetProductRequest
                {
                    Id = id.ToString("D"),
                    Region = scope.Region,
                    Store = scope.Store,
                    GameVersion = scope.GameVersion
                },
                cancellationToken: cancellationToken);

            var price = decimal.Parse(response.Price, NumberStyles.Number, CultureInfo.InvariantCulture);
            var metadata = new Dictionary<string, string>(response.Metadata);

            return new AppCatalogProduct(
                Guid.Parse(response.Id),
                response.Title,
                response.Description,
                MapType(response.Type),
                price,
                response.Currency,
                response.IsActive,
                metadata);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    private static AppProductType MapType(Catalog.Grpc.CatalogProductType type) => type switch
    {
        Catalog.Grpc.CatalogProductType.Item => AppProductType.Item,
        Catalog.Grpc.CatalogProductType.Currency => AppProductType.Currency,
        Catalog.Grpc.CatalogProductType.Subscription => AppProductType.Subscription,
        Catalog.Grpc.CatalogProductType.Bundle => AppProductType.Bundle,
        _ => AppProductType.Item
    };
}
