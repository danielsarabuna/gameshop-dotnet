using Catalog.API.Storage;
using Xunit;

namespace Catalog.API.Tests;

public class CatalogStoreTests
{
    [Fact]
    public void GetConfig_ShouldReturnNull_WhenNothingCached()
    {
        var store = new InMemoryCatalogStore();
        Assert.Null(store.GetConfig("russia", "ru_store", "0.0.36"));
    }

    [Fact]
    public void SetConfig_ShouldStoreAndReturnExactKey()
    {
        var store = new InMemoryCatalogStore();
        var config = new WebShopCatalogConfig(
            GameVersion: "0.0.36",
            Environment: "dev",
            Region: "russia",
            Store: "ru_store",
            UpdatedAt: DateTime.UtcNow.ToString("o"),
            PaymentProviders: Array.Empty<PaymentProviderDto>(),
            Items: new List<CatalogItem>
            {
                new(Guid.NewGuid(), "RU Exclusive Offer", "", CatalogProductType.Currency, 99.00m, "RUB", true, new Dictionary<string, string>(), "/img.png")
            });

        store.SetConfig("russia", "ru_store", "0.0.36", config);

        var result = store.GetConfig("russia", "ru_store", "0.0.36");
        Assert.NotNull(result);
        Assert.Single(result!.Items);
        Assert.Equal("RU Exclusive Offer", result.Items[0].Title);
        Assert.Equal(99.00m, result.Items[0].Price);
    }

    [Fact]
    public void GetItemById_ShouldSearchAllCachedConfigs()
    {
        var store = new InMemoryCatalogStore();
        var itemId = Guid.NewGuid();
        var config = new WebShopCatalogConfig(
            "0.0.36", "dev", "russia", "ru_store", DateTime.UtcNow.ToString("o"),
            new List<PaymentProviderDto> { new("YooKassa", "ЮKassa", true, false, "/y.svg") },
            new List<CatalogItem>
            {
                new(itemId, "Diamond Pack", "", CatalogProductType.Currency, 120m, "RUB", true, new Dictionary<string, string>(), "/d.png")
            });

        store.SetConfig("russia", "ru_store", "0.0.36", config);

        var found = store.GetItemById(itemId);
        Assert.NotNull(found);
        Assert.Equal("Diamond Pack", found!.Title);
    }

    [Fact]
    public void RemoveConfig_ShouldRemoveOnlyTheRequestedKey()
    {
        var store = new InMemoryCatalogStore();
        var config = new WebShopCatalogConfig("0.0.36", "dev", "russia", "ru_store", "", [], []);
        store.SetConfig("russia", "ru_store", "0.0.36", config);
        store.SetConfig("global", "global", "global", config);

        Assert.True(store.RemoveConfig("russia", "ru_store", "0.0.36"));
        Assert.Null(store.GetConfig("russia", "ru_store", "0.0.36"));
        Assert.NotNull(store.GetConfig("global", "global", "global"));
    }
}
