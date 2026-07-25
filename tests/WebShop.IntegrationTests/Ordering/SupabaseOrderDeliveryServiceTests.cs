using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ordering.Application.Abstractions;
using Ordering.Infrastructure.Integrations;

namespace WebShop.IntegrationTests.Ordering;

public sealed class SupabaseOrderDeliveryServiceTests
{
    [Fact]
    public async Task Delivery_uses_one_atomic_rpc_and_converts_legacy_months_to_days()
    {
        var transport = new CaptureHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Supabase:Url"] = "https://example.supabase.co",
            ["Supabase:ServiceRoleKey"] = "test-service-key"
        }).Build();
        var service = new SupabaseOrderDeliveryService(
            new HttpClient(transport), configuration, NullLogger<SupabaseOrderDeliveryService>.Instance);

        await service.DeliverAsync(new SupabaseOrderDelivery(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "MockProvider",
            "mock-payment",
            9.99m,
            "EUR",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new SupabaseOrderItem(
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                "Premium",
                "Subscription",
                1,
                9.99m,
                new Dictionary<string, string> { ["months"] = "2" })]),
            CancellationToken.None);

        Assert.Single(transport.Requests);
        Assert.EndsWith("/rest/v1/rpc/record_webshop_paid_order_v2", transport.Requests[0].Url);
        using var payload = JsonDocument.Parse(transport.Requests[0].Body);
        Assert.Equal(60, payload.RootElement.GetProperty("p_reward_amount").GetInt32());
        var rewards = payload.RootElement.GetProperty("p_reward_items");
        Assert.Single(rewards.EnumerateArray());
        Assert.Equal("subscription_days", rewards[0].GetProperty("kind").GetString());
        Assert.Equal("test-service-key", transport.Requests[0].ApiKey);
    }

    [Fact]
    public async Task Delivery_mixed_order_writes_one_reward_bundle()
    {
        var transport = new CaptureHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Supabase:Url"] = "https://example.supabase.co",
            ["Supabase:ServiceRoleKey"] = "test-service-key"
        }).Build();
        var service = new SupabaseOrderDeliveryService(
            new HttpClient(transport), configuration, NullLogger<SupabaseOrderDeliveryService>.Instance);

        await service.DeliverAsync(new SupabaseOrderDelivery(
            Guid.NewGuid(),
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "MockProvider",
            "mock-payment",
            12.98m,
            "EUR",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [
                new SupabaseOrderItem(Guid.NewGuid(), "75 Diamonds", "Currency", 1, 5.99m,
                    new Dictionary<string, string> { ["diamonds"] = "75" }),
                new SupabaseOrderItem(Guid.NewGuid(), "Premium", "Subscription", 1, 6.99m,
                    new Dictionary<string, string> { ["subscriptionDays"] = "30" })
            ]),
            CancellationToken.None);

        Assert.Single(transport.Requests);
        using var payload = JsonDocument.Parse(transport.Requests[0].Body);
        Assert.Equal("Bundle", payload.RootElement.GetProperty("p_currency_type").GetString());
        var rewards = payload.RootElement.GetProperty("p_reward_items");
        Assert.Equal(2, rewards.GetArrayLength());
        Assert.Equal("diamonds", rewards[0].GetProperty("kind").GetString());
        Assert.Equal("subscription_days", rewards[1].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Delivery_homogeneous_order_falls_back_to_v1_when_v2_rpc_is_not_applied()
    {
        var transport = new CaptureHandler();
        transport.StatusCodes.Enqueue(HttpStatusCode.NotFound);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Supabase:Url"] = "https://example.supabase.co",
            ["Supabase:ServiceRoleKey"] = "test-service-key"
        }).Build();
        var service = new SupabaseOrderDeliveryService(
            new HttpClient(transport), configuration, NullLogger<SupabaseOrderDeliveryService>.Instance);

        await service.DeliverAsync(new SupabaseOrderDelivery(
            Guid.NewGuid(),
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "MockProvider",
            "mock-payment",
            5.99m,
            "EUR",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new SupabaseOrderItem(Guid.NewGuid(), "75 Diamonds", "Currency", 1, 5.99m,
                new Dictionary<string, string> { ["diamonds"] = "75" })]),
            CancellationToken.None);

        Assert.Equal(2, transport.Requests.Count);
        Assert.EndsWith("/rest/v1/rpc/record_webshop_paid_order_v2", transport.Requests[0].Url);
        Assert.EndsWith("/rest/v1/rpc/record_webshop_paid_order", transport.Requests[1].Url);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<(string Url, string Body, string ApiKey)> Requests { get; } = [];
        public Queue<HttpStatusCode> StatusCodes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri!.AbsoluteUri,
                await request.Content!.ReadAsStringAsync(cancellationToken),
                request.Headers.GetValues("apikey").Single()));
            var status = StatusCodes.Count > 0 ? StatusCodes.Dequeue() : HttpStatusCode.NoContent;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.NotFound ? "{\"code\":\"PGRST202\"}" : "")
            };
        }
    }
}
