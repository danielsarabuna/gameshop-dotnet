using Basket.API.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WebShop.IntegrationTests;

/// <summary>
/// Hermetic Basket.API test host: replaces the catalog gRPC client with a deterministic
/// in-memory fake so basket validation is exercised without live infrastructure.
/// </summary>
public sealed class SeededBasketFactory : WebApplicationFactory<Basket.API.Program>
{
    /// <summary>Catalog prices deliberately differ from client-supplied ones to prove overwriting.</summary>
    public static readonly IReadOnlyList<BasketProduct> Products =
    [
        new BasketProduct(
            Guid.Parse("d1a00000-0000-0000-0000-000000000060"),
            "60 Diamonds",
            1.99m,
            "EUR",
            IsActive: true),
        new BasketProduct(
            Guid.Parse("9aa00000-0000-0000-0000-000000000001"),
            "Premium 1 Month",
            4.99m,
            "EUR",
            IsActive: true)
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IBasketCatalogClient>(new FakeBasketCatalogClient(Products));
        });
    }

    private sealed class FakeBasketCatalogClient(IReadOnlyList<BasketProduct> products) : IBasketCatalogClient
    {
        public Task<BasketProduct?> GetProductAsync(Guid productId, CancellationToken cancellationToken)
            => Task.FromResult(products.FirstOrDefault(p => p.Id == productId));
    }
}
