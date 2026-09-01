using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Catalog.API.Tests;

public sealed class AuthConfigurationTests
{
    [Fact]
    public async Task Registers_game_ticket_and_keycloak_schemes_together()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportRSAPublicKeyPem()));
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Auth:Enabled"] = "true",
            ["Auth:Authority"] = "https://identity.example/realms/webshop",
            ["Auth:Audience"] = "webshop-api",
            ["GameTicketJwt:PublicKeyPemBase64"] = publicKey
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWebShopJwtAuthentication(configuration);
        await using var provider = services.BuildServiceProvider();

        var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        Assert.Contains(schemes, scheme => scheme.Name == JwtAuthExtensions.CompositeAuthenticationScheme);
        Assert.Contains(schemes, scheme => scheme.Name == JwtAuthExtensions.GameTicketAuthenticationScheme);
        Assert.Contains(schemes, scheme => scheme.Name == JwtAuthExtensions.KeycloakAuthenticationScheme);
    }

    [Fact]
    public void Forwarded_headers_only_trust_configured_networks()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "172.28.0.0/24",
            ["ForwardedHeaders:ForwardLimit"] = "1"
        });

        var services = new ServiceCollection();
        services.AddWebShopForwardedHeaders(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(1, options.ForwardLimit);
        Assert.Single(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
