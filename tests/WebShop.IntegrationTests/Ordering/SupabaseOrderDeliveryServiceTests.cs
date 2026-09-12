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
        Assert.EndsWith("/rest/v1/rpc/record_webshop_paid_order", transport.Requests[0].Url);
        using var payload = JsonDocument.Parse(transport.Requests[0].Body);
        Assert.Equal(60, payload.RootElement.GetProperty("p_reward_amount").GetInt32());
        Assert.Equal("test-service-key", transport.Requests[0].ApiKey);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<(string Url, string Body, string ApiKey)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri!.AbsoluteUri,
                await request.Content!.ReadAsStringAsync(cancellationToken),
                request.Headers.GetValues("apikey").Single()));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
