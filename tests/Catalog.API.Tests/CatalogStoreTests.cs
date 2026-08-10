using Catalog.API.Storage;
using Xunit;

namespace Catalog.API.Tests;

public class CatalogStoreTests
{
    [Fact]
    public void InMemoryCatalogStore_ShouldProvideInitialSeedItems()
    {
        // Arrange
        var store = new InMemoryCatalogStore();

        // Act
        var items = store.GetAll();

        // Assert
        Assert.NotNull(items);
        Assert.NotEmpty(items);
        Assert.Contains(items, x => x.Title.Contains("diamonds"));
    }

    [Fact]
    public void GetFiltered_ShouldReturnExactRegionAndStoreItems()
    {
        // Arrange
        var store = new InMemoryCatalogStore();
        var customItems = new List<CatalogItem>
        {
            new(Guid.NewGuid(), "RU Exclusive Offer", "Rubles item", CatalogProductType.Currency, 99.00m, "RUB", true, new Dictionary<string, string>(), "/img.png")
        };

        var config = new WebShopCatalogConfig(
            GameVersion: "0.0.36",
            Environment: "dev",
            Region: "russia",
            Store: "ru_store",
            UpdatedAt: DateTime.UtcNow.ToString("o"),
            PaymentProviders: new List<PaymentProviderDto> { new("YooKassa", "ЮKassa", true, false, "/icon.svg") },
            Items: customItems
        );

        // Act
        store.UpdateCatalogConfig("russia", "ru_store", "0.0.36", config);
        var result = store.GetFiltered("russia", "ru_store", "0.0.36");

        // Assert
        Assert.Single(result);
        Assert.Equal("RU Exclusive Offer", result[0].Title);
        Assert.Equal(99.00m, result[0].Price);
    }

    [Fact]
    public void GetPaymentProviders_ShouldReturnConfiguredPaymentGateways()
    {
        // Arrange
        var store = new InMemoryCatalogStore();
        var providers = new List<PaymentProviderDto>
        {
            new("YooKassa", "ЮKassa (Карты МИР, СБП)", true, false, "/yookassa.svg"),
            new("MockProvider", "Тестовая оплата (Dev Sandbox)", true, true, "/mock.svg")
        };

        var config = new WebShopCatalogConfig(
            GameVersion: "0.0.36",
            Environment: "dev",
            Region: "russia",
            Store: "ru_store",
            UpdatedAt: DateTime.UtcNow.ToString("o"),
            PaymentProviders: providers,
            Items: new List<CatalogItem>()
        );

        // Act
        store.UpdateCatalogConfig("russia", "ru_store", "0.0.36", config);
        var result = store.GetPaymentProviders("russia", "ru_store", "0.0.36");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Providers.Count);
        Assert.Contains(result.Providers, p => p.Id == "YooKassa");
        Assert.Contains(result.Providers, p => p.Id == "MockProvider");
    }
}
