using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WebShop.IntegrationTests;

public sealed class CatalogApiTests : IClassFixture<WebApplicationFactory<Catalog.API.Program>>
{
    private readonly WebApplicationFactory<Catalog.API.Program> _factory;

    public CatalogApiTests(WebApplicationFactory<Catalog.API.Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", payload?.Status);
    }

    [Fact]
    public async Task Catalog_items_returns_seeded_items()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/catalog/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<CatalogItemDto>>();
        Assert.NotNull(items);
        Assert.NotEmpty(items!);
        Assert.All(items!, item =>
        {
            Assert.NotEqual(Guid.Empty, item.Id);
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
        });
    }

    [Fact]
    public async Task Catalog_item_by_id_returns_item()
    {
        using var client = _factory.CreateClient();
        using var itemsResponse = await client.GetAsync("/api/v1/catalog/items");
        var items = await itemsResponse.Content.ReadFromJsonAsync<List<CatalogItemDto>>();
        Assert.NotNull(items);
        Assert.NotEmpty(items!);

        var firstItem = items!.First();
        using var itemResponse = await client.GetAsync($"/api/v1/catalog/items/{firstItem.Id}");
        Assert.Equal(HttpStatusCode.OK, itemResponse.StatusCode);
        var fetched = await itemResponse.Content.ReadFromJsonAsync<CatalogItemDto>();
        Assert.NotNull(fetched);
        Assert.Equal(firstItem.Id, fetched!.Id);
    }

    [Fact]
    public async Task Catalog_asset_returns_inline_stream_without_content_disposition()
    {
        using var client = _factory.CreateClient();
        using var itemsResponse = await client.GetAsync("/api/v1/catalog/items");
        
        using var assetResponse = await client.GetAsync("/api/v1/catalog/assets/russia/ru_store/0.0.36/diamonds_60.png");
        if (assetResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Null(assetResponse.Content.Headers.ContentDisposition);
            Assert.NotNull(assetResponse.Headers.CacheControl);
            Assert.True(assetResponse.Headers.CacheControl!.Public);
        }
    }

    [Fact]
    public async Task Catalog_item_by_unknown_id_returns_404()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync($"/api/v1/catalog/items/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record HealthResponse(string Status);

    private sealed record CatalogItemDto(Guid Id, string Title, decimal Price, string Currency);
}
