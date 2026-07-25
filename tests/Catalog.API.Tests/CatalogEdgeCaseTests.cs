using System.Net;
using System.Net.Http;
using Catalog.API.Configuration;
using Catalog.API.Services;
using Catalog.API.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Catalog.API.Tests;

public class CatalogEdgeCaseTests
{
    [Fact]
    public async Task Provider_NetworkException_InitialFetch_ReturnsNullGracefully()
    {
        using var tmp = new TempAssetDir();
        var handler = new AlwaysThrowingHandler();
        var store = new InMemoryCatalogStore();
        var options = new RemoteCatalogOptions
        {
            SupabaseBaseUrl = "https://invalid-supabase.co",
            Bucket = "dev",
            AssetCacheDir = tmp.Path
        };
        var provider = new CatalogProvider(new HttpClient(handler), store, options, NullLogger<CatalogProvider>.Instance);

        var config = await provider.GetOrFetchAsync("russia", "ru_store", "0.0.36", CancellationToken.None);

        Assert.Null(config);
    }

    [Fact]
    public async Task Provider_PathTraversal_In_EnsureAssetDownloadedAsync_ReturnsFalse()
    {
        using var tmp = new TempAssetDir();
        var store = new InMemoryCatalogStore();
        var options = new RemoteCatalogOptions
        {
            SupabaseBaseUrl = "https://mock.supabase.co",
            Bucket = "dev",
            AssetCacheDir = tmp.Path
        };
        var provider = new CatalogProvider(new HttpClient(), store, options, NullLogger<CatalogProvider>.Instance);

        var result = await provider.EnsureAssetDownloadedAsync("russia", "ru_store", "0.0.36", "../../secret.txt", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Provider_MissingAssetOnSupabase_EnsureAssetDownloadedAsync_ReturnsFalse()
    {
        using var tmp = new TempAssetDir();
        var handler = new PerUrlHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["non_existent.png"] = new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var store = new InMemoryCatalogStore();
        var options = new RemoteCatalogOptions
        {
            SupabaseBaseUrl = "https://mock.supabase.co",
            Bucket = "dev",
            AssetCacheDir = tmp.Path
        };
        var provider = new CatalogProvider(new HttpClient(handler), store, options, NullLogger<CatalogProvider>.Instance);

        var result = await provider.EnsureAssetDownloadedAsync("russia", "ru_store", "0.0.36", "non_existent.png", CancellationToken.None);

        Assert.False(result);
    }

    private sealed class AlwaysThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated network outage");
        }
    }

    private sealed class PerUrlHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpResponseMessage> _responses;
        public PerUrlHandler(Dictionary<string, HttpResponseMessage> responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? "";
            foreach (var kv in _responses)
            {
                if (uri.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(kv.Value);
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class TempAssetDir : IDisposable
    {
        public string Path { get; }
        public TempAssetDir() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "catalog-edge-" + Guid.NewGuid().ToString("N")); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
