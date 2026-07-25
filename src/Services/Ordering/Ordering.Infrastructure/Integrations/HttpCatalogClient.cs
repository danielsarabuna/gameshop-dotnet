using System.Net;
using System.Net.Http.Json;
using Ordering.Application.Abstractions;
using Ordering.Domain.Products;

namespace Ordering.Infrastructure.Integrations;

public sealed class HttpCatalogClient : ICatalogClient
{
    private readonly HttpClient _http;

    public HttpCatalogClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken cancellationToken)
        => await GetProductAsync(id, CatalogScope.Default, cancellationToken);

    public async Task<CatalogProduct?> GetProductAsync(Guid id, CatalogScope scope, CancellationToken cancellationToken)
    {
        var query = $"region={Uri.EscapeDataString(scope.Region)}&store={Uri.EscapeDataString(scope.Store)}&gameVersion={Uri.EscapeDataString(scope.GameVersion)}";
        var response = await _http.GetAsync($"/api/v1/catalog/items/{id}?{query}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<CatalogItemDto>(cancellationToken: cancellationToken);
        if (dto is null)
        {
            return null;
        }

        if (!Enum.TryParse<ProductType>(dto.Type, ignoreCase: true, out var type))
        {
            type = ProductType.Item;
        }

        return new CatalogProduct(
            dto.Id,
            dto.Title,
            dto.Description,
            type,
            dto.Price,
            dto.Currency,
            dto.IsActive,
            dto.Metadata ?? new Dictionary<string, string>()
        );
    }

    private sealed record CatalogItemDto(
        Guid Id,
        string Title,
        string Description,
        string Type,
        decimal Price,
        string Currency,
        bool IsActive,
        Dictionary<string, string>? Metadata
    );
}
