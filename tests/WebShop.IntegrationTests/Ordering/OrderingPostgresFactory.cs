using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ordering.Application.Abstractions;

namespace WebShop.IntegrationTests.OrderingApi;

public sealed class OrderingPostgresFactory : WebApplicationFactory<global::Ordering.API.Program>
{
    public StubCatalogClient Catalog { get; } = new();

    public OrderingPostgresFactory(string postgresConnectionString)
    {
        Environment.SetEnvironmentVariable("Ordering__Storage", "Postgres");
        Environment.SetEnvironmentVariable("Ordering__Postgres__ConnectionString", postgresConnectionString);
        Environment.SetEnvironmentVariable("EventBus__Provider", "Null");
        Environment.SetEnvironmentVariable("Catalog__Transport", "Http");
        Environment.SetEnvironmentVariable("Otel__Enabled", "false");
        Environment.SetEnvironmentVariable("Payments__Stripe__SecretKey", "test-payment-token");
        Environment.SetEnvironmentVariable("Payments__MockProvider__Enabled", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:MockProvider:Enabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            var existing = services
                .Where(s => s.ServiceType == typeof(ICatalogClient))
                .ToList();
            foreach (var descriptor in existing)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<ICatalogClient>(Catalog);
        });
    }
}
