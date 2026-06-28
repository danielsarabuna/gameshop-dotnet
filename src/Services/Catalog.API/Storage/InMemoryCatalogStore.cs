using System.Collections.Concurrent;

namespace Catalog.API.Storage;

public sealed class InMemoryCatalogStore : ICatalogStore
{
    private readonly ConcurrentDictionary<string, WebShopCatalogConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    public WebShopCatalogConfig? GetConfig(string region, string store, string gameVersion)
        => _configs.TryGetValue(BuildKey(region, store, gameVersion), out var config) ? config : null;

    public void SetConfig(string region, string store, string gameVersion, WebShopCatalogConfig config)
        => _configs[BuildKey(region, store, gameVersion)] = config;

    public CatalogItem? GetItemById(Guid id)
    {
        foreach (var config in _configs.Values)
        {
            var item = config.Items.FirstOrDefault(x => x.Id == id);
            if (item is not null) return item;
        }
        return null;
    }

    private static string BuildKey(string region, string store, string gameVersion) =>
        $"{region.Trim().ToLowerInvariant()}:{store.Trim().ToLowerInvariant()}:{gameVersion.Trim().ToLowerInvariant()}";
}
