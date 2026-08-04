using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace WebShop.IntegrationTests;

public sealed class OpenApiExposureTests
{
    [Fact]
    public async Task Catalog_exposes_OpenApi_only_in_Development()
    {
        await AssertExposureAsync<Catalog.API.Program>();
    }

    [Fact]
    public async Task Basket_exposes_OpenApi_only_in_Development()
    {
        await AssertExposureAsync<Basket.API.Program>();
    }

    [Fact]
    public async Task Ordering_exposes_OpenApi_only_in_Development()
    {
        await AssertExposureAsync<global::Ordering.API.Program>();
    }

    private static async Task AssertExposureAsync<TProgram>()
        where TProgram : class
    {
        await using var developmentFactory = CreateFactory<TProgram>("Development");
        using var developmentClient = developmentFactory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await developmentClient.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await developmentClient.GetAsync("/scalar/v1")).StatusCode);

        const string privateKeyVariable = "GameTicketJwt__PrivateKeyPemBase64";
        var previousPrivateKey = Environment.GetEnvironmentVariable(privateKeyVariable);
        Environment.SetEnvironmentVariable(privateKeyVariable, CreatePrivateKey());
        try
        {
            await using var productionFactory = CreateFactory<TProgram>("Production");
            using var productionClient = productionFactory.CreateClient();

            Assert.Equal(HttpStatusCode.NotFound, (await productionClient.GetAsync("/openapi/v1.json")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await productionClient.GetAsync("/scalar/v1")).StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(privateKeyVariable, previousPrivateKey);
        }
    }

    private static WebApplicationFactory<TProgram> CreateFactory<TProgram>(string environment)
        where TProgram : class
    {
        return new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GameTicketJwt:PrivateKeyPemBase64"] = CreatePrivateKey(),
                    ["Ordering:Storage"] = "InMemory",
                    ["EventBus:Provider"] = "Null",
                    ["Catalog:Transport"] = "Http",
                    ["Otel:Enabled"] = "false"
                });
            });
        });
    }

    private static string CreatePrivateKey()
    {
        using var rsa = RSA.Create(2048);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportRSAPrivateKeyPem()));
    }
}
