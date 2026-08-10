using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Catalog.API.Configuration;
using Catalog.API.Services;
using Catalog.API.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Catalog.API.Tests;

public class CatalogSyncServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ResponseToReturn);
        }
    }

    [Fact]
    public async Task SyncSingleAsync_ShouldDownloadAndUpdateStore_WhenValidJsonReturned()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        var store = new InMemoryCatalogStore();
        var options = new RemoteCatalogOptions
        {
            SupabaseBaseUrl = "https://mock.supabase.co",
            Bucket = "dev"
        };

        var json = """
        {
          "gameVersion": "0.0.36",
          "environment": "dev",
          "region": "russia",
          "store": "ru_store",
          "updatedAt": "2026-08-10T12:00:00Z",
          "paymentProviders": [
            { "id": "YooKassa", "displayName": "ЮKassa", "isEnabled": true, "isSandbox": false, "iconUrl": "/icon.svg" }
          ],
          "items": [
            {
              "id": "d1a00000-0000-0000-0000-000000000060",
              "sku": "diamonds_60",
              "title": "60 Diamonds Pack",
              "description": "Currency pack",
              "type": "Currency",
              "price": 120.00,
              "currency": "RUB",
              "isActive": true,
              "imageUrl": "/diamonds.png",
              "metadata": { "diamonds": "60" }
            }
          ]
        }
        """;

        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var service = new CatalogSyncService(client, store, options, NullLogger<CatalogSyncService>.Instance);

        // Act
        var success = await service.SyncSingleAsync("russia", "ru_store", "0.0.36", CancellationToken.None);

        // Assert
        Assert.True(success);
        var items = store.GetFiltered("russia", "ru_store", "0.0.36");
        Assert.Single(items);
        Assert.Equal("60 Diamonds Pack", items[0].Title);
        Assert.Equal(120.00m, items[0].Price);
    }

    [Fact]
    public async Task SyncSingleAsync_ShouldHandleNotModified_WithoutError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.NotModified);
        var client = new HttpClient(handler);
        var store = new InMemoryCatalogStore();
        var options = new RemoteCatalogOptions();

        var service = new CatalogSyncService(client, store, options, NullLogger<CatalogSyncService>.Instance);

        // Act
        var result = await service.SyncSingleAsync("russia", "ru_store", "0.0.36", CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SyncSingleAsync_ShouldResolveVersionedImageUrlsToSupabaseCdnUrl()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        var store = new InMemoryCatalogStore();
        var options = new RemoteCatalogOptions
        {
            SupabaseBaseUrl = "https://mock.supabase.co",
            Bucket = "dev"
        };

        var json = """
        {
          "gameVersion": "0.0.36",
          "environment": "dev",
          "region": "russia",
          "store": "ru_store",
          "updatedAt": "2026-08-10T12:00:00Z",
          "paymentProviders": [],
          "items": [
            {
              "id": "d1a00000-0000-0000-0000-000000000060",
              "sku": "diamonds_60",
              "title": "60 Diamonds Pack",
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

        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var service = new CatalogSyncService(client, store, options, NullLogger<CatalogSyncService>.Instance);

        // Act
        var success = await service.SyncSingleAsync("russia", "ru_store", "0.0.36", CancellationToken.None);

        // Assert
        Assert.True(success);
        var items = store.GetFiltered("russia", "ru_store", "0.0.36");
        Assert.Single(items);
        Assert.Equal("https://mock.supabase.co/storage/v1/object/public/dev/russia/ru_store/v0.0.36/webshop/diamonds_60.png", items[0].ImageUrl);
    }
}
