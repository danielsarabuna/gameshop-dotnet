using System.Globalization;
using Catalog.API.Services;
using Catalog.API.Configuration;
using Catalog.Grpc;
using Grpc.Core;

namespace Catalog.API.Grpc;

public sealed class CatalogInternalGrpcService : CatalogInternal.CatalogInternalBase
{
    private readonly ICatalogProvider _provider;
    private readonly SupabaseOptions _options;

    public CatalogInternalGrpcService(ICatalogProvider provider, SupabaseOptions options)
    {
        _provider = provider;
        _options = options;
    }

    public override async Task<CatalogProduct> GetProduct(GetProductRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be a UUID"));
        }

        var region = string.IsNullOrWhiteSpace(request.Region) ? _options.DefaultRegion : request.Region;
        var store = string.IsNullOrWhiteSpace(request.Store) ? _options.DefaultStore : request.Store;
        var gameVersion = string.IsNullOrWhiteSpace(request.GameVersion) ? _options.DefaultGameVersion : request.GameVersion;
        var catalog = await _provider.GetOrFetchAsync(region, store, gameVersion, context.CancellationToken);
        var item = catalog?.Items.FirstOrDefault(candidate => candidate.Id == id);

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

        return product;
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
