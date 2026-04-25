using System.Globalization;
using Catalog.API.Storage;
using Catalog.Grpc;
using Grpc.Core;

namespace Catalog.API.Grpc;

public sealed class CatalogInternalGrpcService : CatalogInternal.CatalogInternalBase
{
    private readonly ICatalogStore _store;

    public CatalogInternalGrpcService(ICatalogStore store)
    {
        _store = store;
    }

    public override Task<CatalogProduct> GetProduct(GetProductRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be a UUID"));
        }

        var item = _store.GetById(id);
        if (item is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Product not found"));
        }

        var product = new CatalogProduct
        {
            Id = item.Id.ToString("D"),
            Title = item.Title,
            Description = item.Description,
            Type = MapType(item.Type),
            Price = item.Price.ToString(CultureInfo.InvariantCulture),
            Currency = item.Currency,
            IsActive = item.IsActive,
            ImageUrl = item.ImageUrl ?? string.Empty
        };

        foreach (var pair in item.Metadata)
        {
            product.Metadata[pair.Key] = pair.Value;
        }

        return Task.FromResult(product);
    }

    private static Catalog.Grpc.CatalogProductType MapType(Catalog.API.Storage.CatalogProductType type) => type switch
    {
        Catalog.API.Storage.CatalogProductType.Item => Catalog.Grpc.CatalogProductType.Item,
        Catalog.API.Storage.CatalogProductType.Currency => Catalog.Grpc.CatalogProductType.Currency,
        Catalog.API.Storage.CatalogProductType.Subscription => Catalog.Grpc.CatalogProductType.Subscription,
        Catalog.API.Storage.CatalogProductType.Bundle => Catalog.Grpc.CatalogProductType.Bundle,
        _ => Catalog.Grpc.CatalogProductType.Unspecified
    };
}
