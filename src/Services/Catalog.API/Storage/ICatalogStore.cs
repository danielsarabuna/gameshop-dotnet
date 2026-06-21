namespace Catalog.API.Storage;

public interface ICatalogStore
{
    IReadOnlyList<CatalogItem> GetAll();
    IReadOnlyList<CatalogItem> GetFiltered(string? region, string? store, string? gameVersion);
    CatalogItem? GetById(Guid id);
    PaymentProviderConfig GetPaymentProviders(string? region, string? store, string? gameVersion);
    void UpdateCatalogConfig(string region, string store, string gameVersion, WebShopCatalogConfig config);
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

public sealed record PaymentProviderDto(
    string Id,
    string DisplayName,
    bool IsEnabled,
    bool IsSandbox,
    string IconUrl
);

public sealed record PaymentProviderConfig(
    IReadOnlyList<PaymentProviderDto> Providers
);

public sealed record WebShopCatalogConfig(
    string GameVersion,
    string Environment,
    string Region,
    string Store,
    string UpdatedAt,
    IReadOnlyList<PaymentProviderDto> PaymentProviders,
    IReadOnlyList<CatalogItem> Items
);
