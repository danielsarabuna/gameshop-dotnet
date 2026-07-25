using Catalog.API.Configuration;
using Catalog.API.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WebShop.IntegrationTests;

/// <summary>
/// Hermetic Catalog.API test host: registers an InMemoryCatalogStore pre-seeded with a known
/// config so tests do not depend on live Supabase availability.
/// </summary>
public sealed class SeededCatalogFactory : WebApplicationFactory<Catalog.API.Program>
{
    public const string DefaultRegion = "russia";
    public const string DefaultStore = "ru_store";
    public const string DefaultVersion = "0.0.36";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureServices(services =>
        {
            // Replace the empty store with a seeded one (last registration wins).
            services.AddSingleton<ICatalogStore>(_ => CreateSeededStore());
            services.AddSingleton(new RemoteCatalogOptions());
        });
    }

    internal static InMemoryCatalogStore CreateSeededStore()
    {
        var store = new InMemoryCatalogStore();
        // Production caches the selected maximum Unity version under this regional key.
        store.SetConfig(DefaultRegion, DefaultStore, "__latest__", new WebShopCatalogConfig(
            GameVersion: DefaultVersion,
            Environment: "test",
            Region: DefaultRegion,
            Store: DefaultStore,
            UpdatedAt: DateTimeOffset.UtcNow.ToString("o"),
            PaymentProviders: [],
            Items:
            [
                new CatalogItem(
                    Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Title: "60 Diamonds",
                    Description: "Test diamond pack",
                    Type: CatalogProductType.Currency,
                    Price: 1.99m,
                    Currency: "EUR",
                    IsActive: true,
                    Metadata: new Dictionary<string, string> { ["diamonds"] = "60" },
                    ImageUrl: null),
                new CatalogItem(
                    Id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Title: "Premium 1 Month",
                    Description: "Test subscription",
                    Type: CatalogProductType.Subscription,
                    Price: 4.99m,
                    Currency: "EUR",
                    IsActive: true,
                    Metadata: new Dictionary<string, string> { ["months"] = "1" },
                    ImageUrl: null)
            ]));
        return store;
    }
}
