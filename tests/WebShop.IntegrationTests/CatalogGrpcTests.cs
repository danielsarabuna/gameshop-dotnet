using System.Globalization;
using Catalog.Grpc;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WebShop.IntegrationTests;

public sealed class CatalogGrpcTests : IClassFixture<WebApplicationFactory<Catalog.API.Program>>
{
    private readonly WebApplicationFactory<Catalog.API.Program> _factory;

    public CatalogGrpcTests(WebApplicationFactory<Catalog.API.Program> factory)
    {
        _factory = factory;
    }

    private CatalogInternal.CatalogInternalClient CreateClient()
    {
        var handler = _factory.Server.CreateHandler();
        var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        return new CatalogInternal.CatalogInternalClient(channel);
    }

    [Fact]
    public async Task GetProduct_returns_NotFound_for_unknown_id()
    {
        var client = CreateClient();
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.GetProductAsync(new GetProductRequest { Id = Guid.NewGuid().ToString("D") });
        });
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetProduct_returns_InvalidArgument_for_non_uuid()
    {
        var client = CreateClient();
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.GetProductAsync(new GetProductRequest { Id = "not-a-uuid" });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task GetProduct_returns_seeded_item()
    {
        // Pull the first id from the REST endpoint to drive the gRPC lookup.
        using var http = _factory.CreateClient();
        using var listResponse = await http.GetAsync("/api/v1/catalog/items");
        listResponse.EnsureSuccessStatusCode();

        using var stream = await listResponse.Content.ReadAsStreamAsync();
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
        var firstId = doc.RootElement[0].GetProperty("id").GetGuid();

        var client = CreateClient();
        var product = await client.GetProductAsync(new GetProductRequest { Id = firstId.ToString("D") });

        Assert.Equal(firstId, Guid.Parse(product.Id));
        Assert.False(string.IsNullOrWhiteSpace(product.Title));
        Assert.NotEqual(Catalog.Grpc.CatalogProductType.Unspecified, product.Type);
        // Price round-trips as invariant decimal string.
        var price = decimal.Parse(product.Price, NumberStyles.Number, CultureInfo.InvariantCulture);
        Assert.True(price > 0);
    }
}
