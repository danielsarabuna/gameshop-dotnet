using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WebShop.IntegrationTests;

public sealed class BasketApiTests : IClassFixture<WebApplicationFactory<Basket.API.Program>>
{
    private readonly WebApplicationFactory<Basket.API.Program> _factory;

    public BasketApiTests(WebApplicationFactory<Basket.API.Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_missing_basket_returns_empty_basket()
    {
        using var client = _factory.CreateClient();
        var userId = $"itest-{Guid.NewGuid():N}";

        using var response = await client.GetAsync($"/api/v1/basket/{userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var basket = await response.Content.ReadFromJsonAsync<BasketDto>();
        Assert.NotNull(basket);
        Assert.Equal(userId, basket!.UserId);
        Assert.Empty(basket.Items);
    }

    [Fact]
    public async Task Upsert_then_get_roundtrips_items()
    {
        using var client = _factory.CreateClient();
        var userId = $"itest-{Guid.NewGuid():N}";

        var update = new UpdateBasketPayload(new[]
        {
            new BasketItemDto(Guid.NewGuid(), "Diamonds 100", 4.99m, 2)
        });

        using var put = await client.PutAsJsonAsync($"/api/v1/basket/{userId}", update);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var get = await client.GetAsync($"/api/v1/basket/{userId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var basket = await get.Content.ReadFromJsonAsync<BasketDto>();
        Assert.NotNull(basket);
        Assert.Single(basket!.Items);
        Assert.Equal(2, basket.Items[0].Quantity);
    }

    [Fact]
    public async Task Delete_clears_basket()
    {
        using var client = _factory.CreateClient();
        var userId = $"itest-{Guid.NewGuid():N}";

        var update = new UpdateBasketPayload(new[]
        {
            new BasketItemDto(Guid.NewGuid(), "Premium", 9.99m, 1)
        });
        using var put = await client.PutAsJsonAsync($"/api/v1/basket/{userId}", update);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var del = await client.DeleteAsync($"/api/v1/basket/{userId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        using var get = await client.GetAsync($"/api/v1/basket/{userId}");
        var basket = await get.Content.ReadFromJsonAsync<BasketDto>();
        Assert.NotNull(basket);
        Assert.Empty(basket!.Items);
    }

    private sealed record BasketItemDto(Guid ProductId, string Title, decimal UnitPrice, int Quantity);

    private sealed record BasketDto(string UserId, List<BasketItemDto> Items);

    private sealed record UpdateBasketPayload(IReadOnlyList<BasketItemDto> Items);
}
