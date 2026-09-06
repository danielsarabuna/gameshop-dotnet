using System.Net;
using Basket.API.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WebShop.IntegrationTests;

public sealed class BasketAuthorizationTests : IClassFixture<BasketAuthorizationTests.AuthEnabledBasketFactory>
{
    private readonly AuthEnabledBasketFactory _factory;

    public BasketAuthorizationTests(AuthEnabledBasketFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_to_game_session_endpoint_is_rejected_when_auth_is_enabled()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync($"/api/v1/basket/{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public sealed class AuthEnabledBasketFactory : WebApplicationFactory<Basket.API.Program>
    {
        public AuthEnabledBasketFactory()
        {
            // Auth is read while Program.cs builds its service container, before test-host
            // configuration callbacks take effect.
            Environment.SetEnvironmentVariable("Auth__Enabled", "true");
            Environment.SetEnvironmentVariable("Auth__Authority", "http://basket-auth-tests.invalid/realms/webshop");
            Environment.SetEnvironmentVariable("Auth__Audience", "webshop-api");
            Environment.SetEnvironmentVariable("Auth__RequireHttpsMetadata", "false");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureServices(services => services.AddSingleton<IBasketCatalogClient>(
                new FakeBasketCatalogClient()));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Environment.SetEnvironmentVariable("Auth__Enabled", null);
                Environment.SetEnvironmentVariable("Auth__Authority", null);
                Environment.SetEnvironmentVariable("Auth__Audience", null);
                Environment.SetEnvironmentVariable("Auth__RequireHttpsMetadata", null);
            }

            base.Dispose(disposing);
        }

        private sealed class FakeBasketCatalogClient : IBasketCatalogClient
        {
            public Task<BasketProduct?> GetProductAsync(Guid productId, CancellationToken cancellationToken)
                => Task.FromResult<BasketProduct?>(null);
        }
    }
}
