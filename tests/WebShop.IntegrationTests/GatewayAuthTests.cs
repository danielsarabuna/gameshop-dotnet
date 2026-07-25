using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace WebShop.IntegrationTests;

public sealed class GatewayAuthTests : IClassFixture<GatewayAuthTests.AuthEnabledGatewayFactory>
{
    private readonly AuthEnabledGatewayFactory _factory;

    public GatewayAuthTests(AuthEnabledGatewayFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_shop_flow_is_rejected_when_auth_is_enabled()
    {
        // The browser shop has no Keycloak session (deeplink + ticket architecture):
        // identity is enforced inside services, so the gateway must not demand a JWT.
        using var client = CreateClient();
        using var response = await client.GetAsync("/api/v1/basket/itest-anonymous");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Public_catalog_route_does_not_challenge_auth()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/api/v1/catalog/items");
        // The catalog backend isn't running in this test, so YARP returns
        // a 5xx Bad Gateway. The point: it must NOT be 401.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_route_is_anonymous_for_external_providers()
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            "/api/v1/webhooks/stripe",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        // External payment providers don't carry a JWT — they're secured by
        // the X-Webhook-Secret header inside Ordering. Auth must not block.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_public_even_when_auth_is_enabled()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public sealed class AuthEnabledGatewayFactory : WebApplicationFactory<WebShop.ApiGateway.Program>
    {
        // Force Auth:Enabled=true via env vars — they're picked up by the
        // default AddEnvironmentVariables() source when WebApplication.CreateBuilder()
        // composes its IConfiguration. ConfigureAppConfiguration callbacks
        // from WebApplicationFactory are applied later (after Program.cs has
        // already read Auth:Enabled), so they wouldn't take effect.
        //
        // Because this mutates process state, test parallelization is disabled
        // assembly-wide (see AssemblyInfo.cs) while these vars are set.
        //
        // The Authority points at a non-resolvable host: JwtBearer only
        // fetches metadata when a token is presented. Tests here only send
        // anonymous requests, so the 401 short-circuits before any HTTP
        // call to Keycloak.
        public AuthEnabledGatewayFactory()
        {
            Environment.SetEnvironmentVariable("Auth__Enabled", "true");
            Environment.SetEnvironmentVariable("Auth__Authority", "http://gateway-auth-tests.invalid/realms/webshop");
            Environment.SetEnvironmentVariable("Auth__Audience", "webshop-api");
            Environment.SetEnvironmentVariable("Auth__RequireHttpsMetadata", "false");
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
    }
}
