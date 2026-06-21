using System.Collections.Concurrent;

namespace Catalog.API.Storage;

public sealed class InMemoryCatalogStore : ICatalogStore
{
    private readonly ConcurrentDictionary<string, WebShopCatalogConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryCatalogStore()
    {
        // По умолчанию засеиваем бановую глобальную конфигурацию из CatalogSeedData
        var defaultProviders = new List<PaymentProviderDto>
        {
            new("YooKassa", "ЮKassa (Карты МИР, СБП)", true, false, "/images/providers/yookassa.svg"),
            new("Xsolla", "Xsolla", true, false, "/images/providers/xsolla.svg"),
            new("MockProvider", "Тестовая оплата (Dev Sandbox)", true, true, "/images/providers/mock.svg")
        };

        var seedConfig = new WebShopCatalogConfig(
            GameVersion: "0.0.36",
            Environment: "dev",
            Region: "russia",
            Store: "ru_store",
            UpdatedAt: DateTime.UtcNow.ToString("o"),
            PaymentProviders: defaultProviders,
            Items: CatalogSeedData.Items
        );

        UpdateCatalogConfig("russia", "ru_store", "0.0.36", seedConfig);
        UpdateCatalogConfig("global", "google_play", "0.0.36", seedConfig);
    }

    public void UpdateCatalogConfig(string region, string store, string gameVersion, WebShopCatalogConfig config)
    {
        var key = BuildKey(region, store, gameVersion);
        _configs[key] = config;
    }

    public IReadOnlyList<CatalogItem> GetAll()
    {
        var active = _configs.Values.FirstOrDefault();
        return active?.Items ?? CatalogSeedData.Items;
    }

    public IReadOnlyList<CatalogItem> GetFiltered(string? region, string? store, string? gameVersion)
    {
        var config = FindConfig(region, store, gameVersion);
        return config?.Items ?? GetAll();
    }

    public CatalogItem? GetById(Guid id)
    {
        foreach (var config in _configs.Values)
        {
            var item = config.Items.FirstOrDefault(x => x.Id == id);
            if (item is not null) return item;
        }
        return CatalogSeedData.Items.FirstOrDefault(x => x.Id == id);
    }

    public PaymentProviderConfig GetPaymentProviders(string? region, string? store, string? gameVersion)
    {
        var config = FindConfig(region, store, gameVersion);
        var providers = config?.PaymentProviders ?? new List<PaymentProviderDto>
        {
            new("MockProvider", "Тестовая оплата (Dev Sandbox)", true, true, "/images/providers/mock.svg")
        };

        return new PaymentProviderConfig(providers);
    }

    private WebShopCatalogConfig? FindConfig(string? region, string? store, string? gameVersion)
    {
        var r = string.IsNullOrWhiteSpace(region) ? "russia" : region.Trim();
        var s = string.IsNullOrWhiteSpace(store) ? "ru_store" : store.Trim();
        var v = string.IsNullOrWhiteSpace(gameVersion) ? "0.0.36" : gameVersion.Trim();

        // 1. Точное совпадение (region, store, gameVersion)
        if (_configs.TryGetValue(BuildKey(r, s, v), out var exact))
            return exact;

        // 2. Фоллбэк на latest для той же регионально-сторовской пары
        if (_configs.TryGetValue(BuildKey(r, s, "latest"), out var latest))
            return latest;

        // 3. Любой совпавший конфиг по региону
        var matchRegion = _configs.Values.FirstOrDefault(c => string.Equals(c.Region, r, StringComparison.OrdinalIgnoreCase));
        if (matchRegion is not null) return matchRegion;

        return _configs.Values.FirstOrDefault();
    }

    private static string BuildKey(string region, string store, string gameVersion) =>
        $"{region.Trim().ToLowerInvariant()}:{store.Trim().ToLowerInvariant()}:{gameVersion.Trim().ToLowerInvariant()}";
}
