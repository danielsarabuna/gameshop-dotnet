using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;

namespace WebShop.IntegrationTests;

public sealed class ExperimentalPaymentWebhookTests
{
    [Fact]
    public async Task Webhook_body_larger_than_limit_returns_413_before_provider_processing()
    {
        var settings = CreateProviderSettings("CorvusPay");
        var orders = new RecordingOrderRepository();
        await using var factory = new WebApplicationFactory<global::Ordering.API.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IOrderRepository>();
                    services.AddSingleton<IOrderRepository>(orders);
                });
            });
        using var client = factory.CreateClient();
        using var body = new StringContent(new string('x', 256 * 1024 + 1), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/v1/webhooks/corvuspay", body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, orders.Reads);
        Assert.Equal(0, orders.Writes);
    }

    [Theory]
    [InlineData("corvuspay", "CorvusPay")]
    public async Task Unsupported_verification_returns_501_without_touching_orders(
        string routeProvider,
        string configurationProvider)
    {
        var settings = CreateProviderSettings(configurationProvider);
        var orders = new RecordingOrderRepository();
        await using var factory = new WebApplicationFactory<global::Ordering.API.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IOrderRepository>();
                    services.AddSingleton<IOrderRepository>(orders);
                });
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            $"/api/v1/webhooks/{routeProvider}",
            new StringContent("{}"));

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal(0, orders.Reads);
        Assert.Equal(0, orders.Writes);
    }

    private static Dictionary<string, string> CreateProviderSettings(string provider)
    {
        var prefix = $"Payments:{provider}:";
        var settings = new Dictionary<string, string>
        {
            [$"{prefix}Enabled"] = "true",
            [$"{prefix}WebhookSecret"] = "test-webhook-secret"
        };

        if (provider == "PayPal")
        {
            settings[$"{prefix}ClientId"] = "test-client";
            settings[$"{prefix}ClientSecret"] = "test-secret";
        }
        else
        {
            settings[$"{prefix}StoreId"] = "test-store";
            settings[$"{prefix}SecretKey"] = "test-secret";
            settings[$"{prefix}ApiUrl"] = "https://example.invalid/payment";
        }

        return settings;
    }

    private sealed class RecordingOrderRepository : IOrderRepository
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }

        public Task AddAsync(Order order, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.CompletedTask;
        }

        public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<Order?>(null);
        }

        public Task UpdateAsync(Order order, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.CompletedTask;
        }

        public Task UpdateWithOutboxAsync(
            Order order,
            IReadOnlyList<OutboxMessage> outbox,
            CancellationToken cancellationToken)
        {
            Writes++;
            return Task.CompletedTask;
        }
    }
}
