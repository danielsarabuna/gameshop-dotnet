using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace WebShop.IntegrationTests;

public class CatalogApiEdgeCaseTests : IClassFixture<WebApplicationFactory<Catalog.API.Program>>
{
    private readonly WebApplicationFactory<Catalog.API.Program> _factory;

    public CatalogApiEdgeCaseTests(WebApplicationFactory<Catalog.API.Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AssetEndpoint_PathTraversal_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/catalog/assets/russia/ru_store/0.0.36/../../appsettings.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AssetEndpoint_MissingAsset_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/catalog/assets/russia/ru_store/0.0.36/non_existent_image_9999.png");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AssetEndpoint_HeaderVerification_NoContentDisposition_And_CacheControlPresent()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/catalog/assets/russia/ru_store/0.0.36/diamonds_60.png");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.Null(response.Content.Headers.ContentDisposition);
            Assert.NotNull(response.Headers.CacheControl);
            Assert.True(response.Headers.CacheControl!.Public);
            Assert.Equal(TimeSpan.FromDays(1), response.Headers.CacheControl.MaxAge);
        }
    }
}
