using System.Text.Json.Serialization;

namespace Catalog.API.Storage;

public interface ICatalogStore
{
    /// <summary>Returns the cached config for the exact (region, store, gameVersion) key, or null if not cached.</summary>
    WebShopCatalogConfig? GetConfig(string region, string store, string gameVersion);

    /// <summary>Caches a freshly fetched config under its (region, store, gameVersion) key.</summary>
    void SetConfig(string region, string store, string gameVersion, WebShopCatalogConfig config);

    /// <summary>Removes the cached config for the exact key.</summary>
    bool RemoveConfig(string region, string store, string gameVersion);

    /// <summary>Searches all cached configs for an item by id (used by gRPC during order placement).</summary>
    CatalogItem? GetItemById(Guid id);
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
    string? ImageUrl,
    [property: JsonIgnore] IReadOnlyDictionary<string, CatalogItemLocalization>? Localizations = null)
{
    public CatalogItem Localize(string? locale)
    {
        if (Localizations is null || Localizations.Count == 0 || string.IsNullOrWhiteSpace(locale))
        {
            return this;
        }

        var translation = Localizations.TryGetValue(locale, out var exact)
            ? exact
            : Localizations.FirstOrDefault(pair => pair.Key.StartsWith(locale.Split('-')[0], StringComparison.OrdinalIgnoreCase)).Value;
        return translation is null ? this : this with { Title = translation.Title, Description = translation.Description };
    }
}

public sealed record CatalogItemLocalization(string Title, string Description);

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
