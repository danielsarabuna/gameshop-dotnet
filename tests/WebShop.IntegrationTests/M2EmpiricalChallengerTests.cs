using System.Net;
using System.Net.Http.Json;
using System.Text;
using Catalog.API.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WebShop.IntegrationTests;

public sealed class M2EmpiricalChallengerTests : IClassFixture<M2EmpiricalChallengerTests.CatalogTestFixture>
{
    private readonly CatalogTestFixture _fixture;

    public M2EmpiricalChallengerTests(CatalogTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region 1. Gateway Auth Route Verification

    [Fact]
    public async Task Gateway_Auth_ClaimTicket_Route_Is_Anonymous_And_Mapped()
    {
        using var factory = new GatewayAuthTests.AuthEnabledGatewayFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.PostAsync(
            "/api/v1/auth/claim-ticket?ticket=00000000-0000-0000-0000-000000000000",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        // Must NOT be 401 Unauthorized (proves anonymous policy)
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        // Must NOT be 404 Not Found from YARP router
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_Auth_VerifyPlayer_Route_Is_Anonymous_And_Mapped()
    {
        using var factory = new GatewayAuthTests.AuthEnabledGatewayFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.PostAsync(
            "/api/v1/auth/verify-player?userId=player_123",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        // Must NOT be 401 Unauthorized (proves anonymous policy)
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        // Must NOT be 404 Not Found from YARP router
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Gateway_Auth_Routes_Pass_Various_HTTP_Methods(string method)
    {
        using var factory = new GatewayAuthTests.AuthEnabledGatewayFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/auth/claim-ticket?ticket=12345");
        using var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_Auth_ClaimTicket_Endpoint_Direct_Call_Returns_BadRequest_For_Invalid_Ticket()
    {
        using var client = _fixture.CreateClient();
        var ticket = Guid.NewGuid();
        using var response = await client.PostAsync($"/api/v1/auth/claim-ticket?ticket={ticket}", null);

        // Since verifier returns invalid for random ticket in mock/dev env, should return 400 Bad Request (not 404 or 500)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_Auth_VerifyPlayer_Endpoint_Direct_Call_Returns_BadRequest_For_Unknown_Player()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.PostAsync("/api/v1/auth/verify-player?userId=unknown_player_999", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region 2. Path Traversal Edge Cases & Security Attacks

    [Theory]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("%2e%2e")]
    [InlineData("%2e%2e/%2e%2e")]
    [InlineData("....//")]
    [InlineData("....//....//secret.txt")]
    [InlineData("App_Data/catalog-assets-secret")]
    [InlineData("../catalog-assets-secret/secret.txt")]
    [InlineData("..%2f..%2fsecret.txt")]
    [InlineData("..%5c..%5csecret.txt")]
    public async Task Asset_Proxy_Path_Traversal_Attacks_Return_Strict_404(string traversalPath)
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync($"/api/v1/catalog/assets/russia/ru_store/0.0.36/{traversalPath}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Asset_Proxy_Sibling_Directory_Prefix_Matching_Attack_Returns_404()
    {
        // Simulates an attacker targeting a sibling directory named "catalog-assets-secret"
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync("/api/v1/catalog/assets/..-secret/ru_store/0.0.36/secret.txt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region 3. MIME Type & Caching Headers Verification

    [Theory]
    [InlineData("test_asset.png", "image/png")]
    [InlineData("test_asset.jpg", "image/jpeg")]
    [InlineData("test_asset.jpeg", "image/jpeg")]
    [InlineData("test_asset.webp", "image/webp")]
    [InlineData("test_asset.svg", "image/svg+xml")]
    [InlineData("test_asset.gif", "image/gif")]
    [InlineData("test_asset.unknown", "application/octet-stream")]
    public async Task Asset_Proxy_Returns_Correct_MIME_Type_And_Cache_Headers(string filename, string expectedMimeType)
    {
        _fixture.EnsureAssetExists("russia", "ru_store", "0.0.36", filename, "DUMMY_CONTENT");

        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync($"/api/v1/catalog/assets/russia/ru_store/0.0.36/{filename}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify Cache-Control Header
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl.Public, "Cache-Control should be public");
        Assert.Equal(TimeSpan.FromSeconds(86400), response.Headers.CacheControl.MaxAge);
        Assert.True(response.Headers.CacheControl.Extensions.Any(e => e.Name == "immutable"), "Cache-Control should contain immutable");

        // Verify Content-Type Header
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal(expectedMimeType, response.Content.Headers.ContentType.MediaType);
    }

    #endregion

    public sealed class CatalogTestFixture : WebApplicationFactory<Catalog.API.Program>
    {
        public string TempAssetDir { get; }

        public CatalogTestFixture()
        {
            TempAssetDir = Path.Combine(Path.GetTempPath(), "m2-challenger-assets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempAssetDir);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(RemoteCatalogOptions));
                services.AddSingleton(new RemoteCatalogOptions
                {
                    AssetCacheDir = TempAssetDir
                });
            });
        }

        public void EnsureAssetExists(string region, string store, string version, string filename, string content)
        {
            var dir = Path.Combine(TempAssetDir, region, store, version);
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, filename);
            File.WriteAllText(filePath, content);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (Directory.Exists(TempAssetDir))
                    {
                        Directory.Delete(TempAssetDir, true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            base.Dispose(disposing);
        }
    }
}
