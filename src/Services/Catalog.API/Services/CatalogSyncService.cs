using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Catalog.API.Configuration;
using Catalog.API.Storage;

namespace Catalog.API.Services;

public interface ICatalogSyncService
{
    Task SyncAllAsync(CancellationToken cancellationToken);
    Task<bool> SyncSingleAsync(string region, string store, string gameVersion, CancellationToken cancellationToken);
}

public sealed class CatalogSyncService : ICatalogSyncService
{
    private readonly HttpClient _httpClient;
    private readonly ICatalogStore _catalogStore;
    private readonly RemoteCatalogOptions _options;
    private readonly ILogger<CatalogSyncService> _logger;

    // Хранит ETag для каждого (region, store, version)
    private readonly ConcurrentDictionary<string, string> _eTags = new(StringComparer.OrdinalIgnoreCase);

    public CatalogSyncService(
        HttpClient httpClient,
        ICatalogStore catalogStore,
        RemoteCatalogOptions options,
        ILogger<CatalogSyncService> logger)
    {
        _httpClient = httpClient;
        _catalogStore = catalogStore;
        _options = options;
        _logger = logger;
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken)
    {
        foreach (var region in _options.Regions)
        {
            foreach (var store in _options.Stores)
            {
                foreach (var gameVersion in _options.GameVersions)
                {
                    await SyncSingleAsync(region, store, gameVersion, cancellationToken);
                }
            }
        }
    }

    public async Task<bool> SyncSingleAsync(string region, string store, string gameVersion, CancellationToken cancellationToken)
    {
        var cacheKey = $"{region}:{store}:{gameVersion}";
        var fileName = $"webshop_config_{gameVersion}.json";

        var url = $"{_options.SupabaseBaseUrl.TrimEnd('/')}/storage/v1/object/public/{_options.Bucket}/{region}/{store}/{fileName}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (_eTags.TryGetValue(cacheKey, out var etag))
            {
                request.Headers.IfNoneMatch.ParseAdd(etag);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // Конфиг не изменился
                return false;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Конфиг для данной версии/региона ещё не выгружен
                return false;
            }

            response.EnsureSuccessStatusCode();

            var jsonText = await response.Content.ReadAsStringAsync(cancellationToken);
            var newETag = response.Headers.ETag?.Tag;

            var dto = JsonSerializer.Deserialize<WebShopCatalogJsonDto>(jsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto is null || dto.Items is null)
            {
                _logger.LogWarning("Загружен пустой или некорректный JSON конфиг каталога по адресу {Url}", url);
                return false;
            }

            var domainItems = dto.Items.Select(item => MapToDomainItem(item, region, store)).ToList();
            var providers = dto.PaymentProviders?.Select(MapToDomainProvider).ToList() ?? new List<PaymentProviderDto>();

            var config = new WebShopCatalogConfig(
                GameVersion: dto.GameVersion ?? gameVersion,
                Environment: dto.Environment ?? _options.Bucket,
                Region: dto.Region ?? region,
                Store: dto.Store ?? store,
                UpdatedAt: dto.UpdatedAt ?? DateTime.UtcNow.ToString("o"),
                PaymentProviders: providers,
                Items: domainItems
            );

            _catalogStore.UpdateCatalogConfig(region, store, gameVersion, config);

            if (!string.IsNullOrEmpty(newETag))
            {
                _eTags[cacheKey] = newETag;
            }

            _logger.LogInformation("Успешно обновлён каталог для ({Region}/{Store}/{Version}). Загружено {ItemCount} товаров и {ProviderCount} провайдеров.",
                region, store, gameVersion, domainItems.Count, providers.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось синхронизировать каталог по адресу {Url}", url);
            return false;
        }
    }

    private CatalogItem MapToDomainItem(CatalogItemJsonDto dto, string region, string store)
    {
        Enum.TryParse<CatalogProductType>(dto.Type, true, out var productType);
        var metadata = dto.Metadata ?? new Dictionary<string, string>();
        var imageUrl = dto.ImageUrl;

        if (!string.IsNullOrWhiteSpace(imageUrl) &&
            !imageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !imageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !imageUrl.StartsWith("/"))
        {
            var baseUrl = _options.SupabaseBaseUrl.TrimEnd('/');
            imageUrl = $"{baseUrl}/storage/v1/object/public/{_options.Bucket}/{region}/{store}/{imageUrl.TrimStart('/')}";
        }

        return new CatalogItem(
            Id: Guid.TryParse(dto.Id, out var parsedGuid) ? parsedGuid : Guid.NewGuid(),
            Title: dto.Title ?? "Unknown Offer",
            Description: dto.Description ?? "",
            Type: productType,
            Price: dto.Price,
            Currency: dto.Currency ?? "EUR",
            IsActive: dto.IsActive,
            Metadata: metadata,
            ImageUrl: imageUrl
        );
    }

    private static PaymentProviderDto MapToDomainProvider(PaymentProviderJsonDto dto)
    {
        return new PaymentProviderDto(
            Id: dto.Id ?? "MockProvider",
            DisplayName: dto.DisplayName ?? dto.Id ?? "Payment Gateway",
            IsEnabled: dto.IsEnabled,
            IsSandbox: dto.IsSandbox,
            IconUrl: dto.IconUrl ?? "/images/providers/default.svg"
        );
    }

    private sealed class WebShopCatalogJsonDto
    {
        public string? GameVersion { get; set; }
        public string? Environment { get; set; }
        public string? Region { get; set; }
        public string? Store { get; set; }
        public string? UpdatedAt { get; set; }
        public List<PaymentProviderJsonDto>? PaymentProviders { get; set; }
        public List<CatalogItemJsonDto>? Items { get; set; }
    }

    private sealed class PaymentProviderJsonDto
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsSandbox { get; set; }
        public string? IconUrl { get; set; }
    }

    private sealed class CatalogItemJsonDto
    {
        public string? Id { get; set; }
        public string? Sku { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }
        public decimal Price { get; set; }
        public string? Currency { get; set; }
        public bool IsActive { get; set; }
        public string? ImageUrl { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
