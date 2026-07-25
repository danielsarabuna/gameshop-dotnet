using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Catalog.API.Configuration;
using Catalog.API.Services;
using Catalog.API.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Catalog.API.Tests;

public class CatalogProviderTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Routes each request to a canned response based on the URL substring (fileName),
    /// emulating Supabase Storage serving a webshop catalog.
    /// </summary>
    private sealed class PerUrlHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpResponseMessage> _responses;
        private readonly IReadOnlyList<string> _publishedVersions;

        public PerUrlHandler(Dictionary<string, HttpResponseMessage> responses, params string[] publishedVersions)
        {
            _responses = responses;
            _publishedVersions = publishedVersions.Length == 0 ? ["0.0.36"] : publishedVersions;
        }

        // Track which URLs were actually hit (asset downloads, config fetches).
        public List<string> HitUrls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? "";
            HitUrls.Add(uri);
            if (request.Method == HttpMethod.Post && uri.Contains("/object/list/", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonOk(JsonSerializer.Serialize(_publishedVersions.Select(version => new { name = $"webshop_config_{version}.json" }))));
            }
            foreach (var kv in _responses)
            {
                if (uri.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(kv.Value);
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        }
    }

    private const string ValidCatalogJson = """
        {
          "gameVersion": "0.0.36",
          "environment": "dev",
          "region": "russia",
          "store": "ru_store",
          "updatedAt": "2026-08-11T00:00:00Z",
          "paymentProviders": [
            { "id": "YooKassa", "displayName": "ЮKassa", "isEnabled": true, "isSandbox": false, "iconUrl": "/y.svg" }
          ],
          "items": [
            {
              "id": "d1a00000-0000-0000-0000-000000000060",
              "sku": "diamonds_60",
              "title": "60 Diamonds",
              "type": "Currency",
              "price": 120.00,
              "currency": "RUB",
              "isActive": true,
              "imageUrl": "v0.0.36/webshop/diamonds_60.png",
              "metadata": { "diamonds": "60" }
            }
          ]
        }
        """;

    private const string UnityLocalizedCatalogJson = """
        {
          "gameVersion": "0.0.1",
          "defaultLocale": "en-US",
          "items": [
            {
              "id": "d1a00000-0000-0000-0000-000000000060",
              "sku": "diamonds_60",
              "type": "Currency",
              "price": 120.00,
              "currency": "RUB",
              "isActive": true,
              "metadata": { "diamonds": "60" },
              "locales": {
                "en-US": { "title": "60 Diamonds", "description": "Currency pack" },
                "ru-RU": { "title": "60 алмазов", "description": "Набор алмазов" }
              }
            }
          ]
        }
        """;

    private static RemoteCatalogOptions TempOptions(string assetDir) => new()
    {
        SupabaseBaseUrl = "https://mock.supabase.co",
        Bucket = "dev",
        AssetCacheDir = assetDir
    };

    [Fact]
    public async Task GetOrFetchAsync_ShouldLoadCatalog_AndDownloadAsset_AndRewriteImageUrl()
    {
        using var tmp = new TempAssetDir();
        var pngBytes = Encoding.UTF8.GetBytes("FAKE-PNG");
        var handler = new PerUrlHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["webshop_config_0.0.36.json"] = JsonOk(ValidCatalogJson),
            ["diamonds_60.png"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pngBytes) },
        });
        var store = new InMemoryCatalogStore();
        var provider = new CatalogProvider(new HttpClient(handler), store, TempOptions(tmp.Path), NullLogger<CatalogProvider>.Instance);

        var config = await provider.GetOrFetchAsync("russia", "ru_store", "0.0.36", CancellationToken.None);

        Assert.NotNull(config);
        var item = Assert.Single(config!.Items);
        Assert.Equal("60 Diamonds", item.Title);
        Assert.Equal(120.00m, item.Price);
        Assert.Equal("RUB", item.Currency);
        // Image URL rewritten to the cached-assets endpoint preserving subpath hierarchy.
        Assert.Equal("/api/v1/catalog/assets/russia/ru_store/0.0.36/v0.0.36/webshop/diamonds_60.png", item.ImageUrl);
        // The asset was downloaded to disk preserving directory structure.
        Assert.True(File.Exists(Path.Combine(tmp.Path, "russia", "ru_store", "0.0.36", "v0.0.36", "webshop", "diamonds_60.png")));
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldReturnNull_AndLog_WhenConfigMissingForVersion()
    {
        using var tmp = new TempAssetDir();
        var handler = new PerUrlHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["webshop_config_0.0.99.json"] = new HttpResponseMessage(HttpStatusCode.BadRequest),
        }, Array.Empty<string>());
        var store = new InMemoryCatalogStore();
        var provider = new CatalogProvider(new HttpClient(handler), store, TempOptions(tmp.Path), NullLogger<CatalogProvider>.Instance);

        var config = await provider.GetOrFetchAsync("russia", "ru_store", "0.0.99", CancellationToken.None);

        Assert.Null(config);
        Assert.Null(store.GetConfig("russia", "ru_store", "__latest__"));
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldLoadOnlyTheWebshopConfig()
    {
        using var tmp = new TempAssetDir();
        var handler = new PerUrlHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["webshop_config_0.0.36.json"] = JsonOk(ValidCatalogJson),
            ["diamonds_60.png"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("X")) },
        });
        var store = new InMemoryCatalogStore();
        var provider = new CatalogProvider(new HttpClient(handler), store, TempOptions(tmp.Path), NullLogger<CatalogProvider>.Instance);

        var config = await provider.GetOrFetchAsync("russia", "ru_store", "0.0.36", CancellationToken.None);

        Assert.NotNull(config);
        Assert.Single(config!.Items);
        Assert.Contains("YooKassa", config.PaymentProviders.Select(p => p.Id));
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldAcceptUnityLocalizedOffers_AndSelectRequestedLocale()
    {
        using var tmp = new TempAssetDir();
        var handler = new PerUrlHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["webshop_config_0.0.1.json"] = JsonOk(UnityLocalizedCatalogJson),
        }, "0.0.1");
        var provider = new CatalogProvider(
            new HttpClient(handler), new InMemoryCatalogStore(), TempOptions(tmp.Path), NullLogger<CatalogProvider>.Instance);

        var config = await provider.GetOrFetchAsync("russia", "ru_store", "0.0.1", CancellationToken.None);

        var item = Assert.Single(config!.Items).Localize("ru-RU");
        Assert.Equal("60 алмазов", item.Title);
        Assert.Equal("Набор алмазов", item.Description);
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldServeFromCache_OnSecondCall()
    {
        using var tmp = new TempAssetDir();
        var handler = new PerUrlHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["webshop_config_0.0.36.json"] = JsonOk(ValidCatalogJson),
            ["diamonds_60.png"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("X")) },
        });
        var store = new InMemoryCatalogStore();
        var provider = new CatalogProvider(new HttpClient(handler), store, TempOptions(tmp.Path), NullLogger<CatalogProvider>.Instance);

        await provider.GetOrFetchAsync("russia", "ru_store", "0.0.36", CancellationToken.None);
        var hitsBefore = handler.HitUrls.Count(u => u.Contains("webshop_config"));
        await provider.GetOrFetchAsync("russia", "ru_store", "0.0.36", CancellationToken.None);
        var hitsAfter = handler.HitUrls.Count(u => u.Contains("webshop_config"));

        // Second call served from in-memory cache → no additional Supabase fetch.
        Assert.Equal(hitsBefore, hitsAfter);
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldFetchAgain_AfterInvalidation()
    {
        using var tmp = new TempAssetDir();
        var handler = new CountingCatalogHandler();
        var store = new InMemoryCatalogStore();
        var provider = new CatalogProvider(new HttpClient(handler), store, TempOptions(tmp.Path), NullLogger<CatalogProvider>.Instance);

        await provider.GetOrFetchAsync("russia", "ru_store", "0.0.36", CancellationToken.None);
        // The event names the newly published version, while the cached key is regional latest.
        Assert.True(await provider.InvalidateAsync("russia", "ru_store", "0.0.37", CancellationToken.None));
        await provider.GetOrFetchAsync("russia", "ru_store", "0.0.36", CancellationToken.None);

        Assert.Equal(2, handler.ConfigRequestCount);
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldChooseLatestPublishedVersion_RegardlessOfRequestedVersion()
    {
        using var tmp = new TempAssetDir();
        var oldCatalog = ValidCatalogJson.Replace("0.0.36", "0.0.1").Replace("120.00", "1.00");
        var handler = new PerUrlHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["webshop_config_0.0.1.json"] = JsonOk(oldCatalog),
            ["webshop_config_0.0.36.json"] = JsonOk(ValidCatalogJson),
            ["diamonds_60.png"] = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("X")) },
        }, "0.0.1", "0.0.36");

        var provider = new CatalogProvider(new HttpClient(handler), new InMemoryCatalogStore(), TempOptions(tmp.Path), NullLogger<CatalogProvider>.Instance);
        var config = await provider.GetOrFetchAsync("russia", "ru_store", "0.0.1", CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal("0.0.36", config!.GameVersion);
        Assert.Equal(120.00m, Assert.Single(config.Items).Price);
        Assert.Contains(handler.HitUrls, url => url.Contains("webshop_config_0.0.36.json", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.HitUrls, url => url.Contains("webshop_config_0.0.1.json", StringComparison.Ordinal));
    }

    private sealed class CountingCatalogHandler : HttpMessageHandler
    {
        public int ConfigRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? "";
            if (request.Method == HttpMethod.Post && uri.Contains("/object/list/", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonOk("[{\"name\":\"webshop_config_0.0.36.json\"}]"));
            }
            if (uri.Contains("webshop_config", StringComparison.OrdinalIgnoreCase))
            {
                ConfigRequestCount++;
                return Task.FromResult(JsonOk(ValidCatalogJson));
            }
            if (uri.Contains("diamonds_60.png", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("X"))
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        }
    }

    private static HttpResponseMessage JsonOk(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class TempAssetDir : IDisposable
    {
        public string Path { get; }
        public TempAssetDir() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "catalog-assets-" + Guid.NewGuid().ToString("N")); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
