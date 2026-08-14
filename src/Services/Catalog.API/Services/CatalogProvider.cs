using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Catalog.API.Configuration;
using Catalog.API.Storage;

namespace Catalog.API.Services;

public interface ICatalogProvider
{
    /// <summary>
    /// Returns the catalog config for (region, store, gameVersion), fetching it lazily from
    /// Supabase on first access (with disk-cached assets). Returns null when no valid config
    /// exists for the requested version — the caller should surface an empty catalog.
    /// </summary>
    Task<WebShopCatalogConfig?> GetOrFetchAsync(string region, string store, string gameVersion, CancellationToken ct);

    /// <summary>Forces a fresh fetch bypassing the TTL cache (used by the /reload endpoint).</summary>
    Task<WebShopCatalogConfig?> ForceRefreshAsync(string region, string store, string gameVersion, CancellationToken ct);

    /// <summary>Ensures the specified relative asset is downloaded from Supabase to disk cache.</summary>
    Task<bool> EnsureAssetDownloadedAsync(string region, string store, string gameVersion, string relativePath, CancellationToken ct);
}

public sealed class CatalogProvider : ICatalogProvider
{
    private readonly HttpClient _http;
    private readonly ICatalogStore _store;
    private readonly RemoteCatalogOptions _options;
    private readonly ILogger<CatalogProvider> _logger;

    // Per-key freshness + ETag for conditional revalidation once the TTL elapses.
    private readonly ConcurrentDictionary<string, (DateTime FetchedAt, string? ETag)> _meta = new(StringComparer.OrdinalIgnoreCase);
    // Guards against duplicate concurrent fetches for the same key.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public CatalogProvider(HttpClient http, ICatalogStore store, RemoteCatalogOptions options, ILogger<CatalogProvider> logger)
    {
        _http = http;
        _store = store;
        _options = options;
        _logger = logger;
    }

    public async Task<WebShopCatalogConfig?> GetOrFetchAsync(string region, string store, string gameVersion, CancellationToken ct)
    {
        var key = BuildKey(region, store, gameVersion);

        var cached = _store.GetConfig(region, store, gameVersion);
        if (cached is not null && _meta.TryGetValue(key, out var meta)
            && (DateTime.UtcNow - meta.FetchedAt).TotalSeconds < _options.CacheTtlSeconds)
        {
            return cached; // fresh cache hit
        }

        // Stale-or-miss → fetch under a per-key lock (ETag revalidation if we have a stale copy).
        return await FetchUnderLockAsync(region, store, gameVersion, key, useETag: cached is not null, ct);
    }

    public Task<WebShopCatalogConfig?> ForceRefreshAsync(string region, string store, string gameVersion, CancellationToken ct)
        => FetchUnderLockAsync(region, store, gameVersion, BuildKey(region, store, gameVersion), useETag: false, ct);

    private async Task<WebShopCatalogConfig?> FetchUnderLockAsync(string region, string store, string gameVersion, string key, bool useETag, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Re-check after acquiring the lock (another caller may have just fetched).
            if (!useETag)
            {
                var cached = _store.GetConfig(region, store, gameVersion);
                if (cached is not null && _meta.TryGetValue(key, out var meta)
                    && (DateTime.UtcNow - meta.FetchedAt).TotalSeconds < _options.CacheTtlSeconds)
                {
                    return cached;
                }
            }

            return await FetchFromSupabaseAsync(region, store, gameVersion, key, useETag, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<WebShopCatalogConfig?> FetchFromSupabaseAsync(string region, string store, string gameVersion, string key, bool useETag, CancellationToken ct)
    {
        // webshop_config_*.json is the real shop catalog; config_*.json is the game content
        // config (different schema). Try the shop config first; fall back to config_* only if it
        // actually carries catalog data (validation below rejects the game config).
        var candidates = new[] { $"webshop_config_{gameVersion}.json", $"config_{gameVersion}.json" };
        string? existingETag = useETag && _meta.TryGetValue(key, out var m) ? m.ETag : null;
        var existingCached = _store.GetConfig(region, store, gameVersion);

        foreach (var fileName in candidates)
        {
            var url = $"{_options.SupabaseBaseUrl.TrimEnd('/')}/storage/v1/object/public/{_options.Bucket}/{region}/{store}/{fileName}";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(existingETag))
                {
                    request.Headers.IfNoneMatch.ParseAdd(existingETag);
                }

                using var response = await _http.SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    // Revalidate the freshness timestamp; the cached config is still current.
                    if (_meta.TryGetValue(key, out var oldMeta))
                    {
                        _meta[key] = (DateTime.UtcNow, oldMeta.ETag);
                    }
                    return existingCached;
                }

                // Supabase returns 400 (not 404) for missing public objects — treat both as "absent".
                if (response.StatusCode == HttpStatusCode.NotFound || (int)response.StatusCode == 400)
                {
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var jsonText = await response.Content.ReadAsStringAsync(ct);
                var dto = JsonSerializer.Deserialize<WebShopCatalogJsonDto>(jsonText, JsonOpts);

                if (!IsValidCatalog(dto))
                {
                    // Not a real webshop catalog (e.g. the game's config_*.json) — try next candidate.
                    continue;
                }

                var newETag = response.Headers.ETag?.Tag;
                var domainItems = await BuildDomainItemsAsync(dto!, region, store, gameVersion, ct);
                var providers = dto!.PaymentProviders?.Select(MapToDomainProvider).ToList() ?? new List<PaymentProviderDto>();

                var config = new WebShopCatalogConfig(
                    GameVersion: dto!.GameVersion ?? gameVersion,
                    Environment: dto.Environment ?? _options.Bucket,
                    Region: dto.Region ?? region,
                    Store: dto.Store ?? store,
                    UpdatedAt: dto.UpdatedAt ?? DateTime.UtcNow.ToString("o"),
                    PaymentProviders: providers,
                    Items: domainItems
                );

                _store.SetConfig(region, store, gameVersion, config);
                _meta[key] = (DateTime.UtcNow, newETag);

                _logger.LogInformation(
                    "Каталог загружен с Supabase для {Region}/{Store}/{Version}: {ItemCount} офферов, {ProviderCount} провайдеров.",
                    region, store, gameVersion, domainItems.Count, providers.Count);

                return config;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось загрузить каталог по адресу {Url}", url);
            }
        }

        if (existingCached is not null)
        {
            _logger.LogWarning(
                "Ошибка обновления с Supabase для region={Region} store={Store} gameVersion={Version}. Возвращен ранее закэшированный каталог.",
                region, store, gameVersion);
            return existingCached;
        }

        // Nothing valid found. Logged at Warning so suspicious/wrong versions are visible in dev AND prod.
        _logger.LogWarning(
            "Конфиг каталога не найден или невалиден для region={Region} store={Store} gameVersion={Version}. Возвращён пустой каталог.",
            region, store, gameVersion);

        return null;
    }

    private static bool IsValidCatalog(WebShopCatalogJsonDto? dto)
        => dto is { Items: { Count: > 0 } };

    private async Task<List<CatalogItem>> BuildDomainItemsAsync(WebShopCatalogJsonDto dto, string region, string store, string gameVersion, CancellationToken ct)
    {
        var items = new List<CatalogItem>();
        foreach (var item in dto.Items ?? Enumerable.Empty<CatalogItemJsonDto>())
        {
            var (resolvedUrl, needsDownload, relativePath) = ResolveImageUrl(item.ImageUrl, region, store, gameVersion);

            if (needsDownload && !string.IsNullOrEmpty(relativePath))
            {
                await EnsureAssetDownloadedAsync(region, store, gameVersion, relativePath, ct);
            }

            items.Add(MapToDomainItem(item, region, store, resolvedUrl));
        }
        return items;
    }

    private (string? resolvedUrl, bool needsDownload, string? relativePath) ResolveImageUrl(string? imageUrl, string region, string store, string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return (null, false, null);
        }

        // Absolute URLs (http/https) and root-absolute paths (/...) are left untouched.
        if (imageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || imageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || imageUrl.StartsWith('/'))
        {
            return (imageUrl, false, null);
        }

        // Relative path (e.g. "v0.0.36/webshop/diamonds_60.png") → download + rewrite to cached endpoint preserving subpaths.
        var relativePath = imageUrl.TrimStart('/');
        var localEndpoint = $"/api/v1/catalog/assets/{region}/{store}/{gameVersion}/{relativePath}";
        return (localEndpoint, true, relativePath);
    }

    public async Task<bool> EnsureAssetDownloadedAsync(string region, string store, string gameVersion, string relativePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var cleanRel = relativePath.TrimStart('/');
        var subDir = Path.GetDirectoryName(cleanRel) ?? "";
        var localDir = Path.Combine(_options.AssetCacheDir, region, store, gameVersion, subDir);
        var localPath = Path.Combine(_options.AssetCacheDir, region, store, gameVersion, cleanRel);

        // Path-traversal guard: the resolved path must stay inside the cache root.
        var root = Path.GetFullPath(_options.AssetCacheDir);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }
        var resolved = Path.GetFullPath(localPath);
        if (!resolved.StartsWith(root, StringComparison.Ordinal))
        {
            _logger.LogWarning("Путь ассета выходит за пределы кэша: {Path}", resolved);
            return false;
        }

        if (File.Exists(resolved))
        {
            return true; // already cached on disk
        }

        var sourceUrl = $"{_options.SupabaseBaseUrl.TrimEnd('/')}/storage/v1/object/public/{_options.Bucket}/{region}/{store}/{cleanRel}";
        Directory.CreateDirectory(localDir);

        try
        {
            var bytes = await _http.GetByteArrayAsync(sourceUrl, ct);
            await File.WriteAllBytesAsync(resolved, bytes, ct);
            _logger.LogDebug("Ассет закэширован: {Path} ({Bytes} байт)", resolved, bytes.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось скачать ассет {Url}", sourceUrl);
            return false;
        }
    }

    private static CatalogItem MapToDomainItem(CatalogItemJsonDto dto, string region, string store, string? resolvedImageUrl)
    {
        Enum.TryParse<CatalogProductType>(dto.Type, true, out var productType);
        var metadata = dto.Metadata ?? new Dictionary<string, string>();
        return new CatalogItem(
            Id: Guid.TryParse(dto.Id, out var parsedGuid) ? parsedGuid : Guid.NewGuid(),
            Title: string.IsNullOrWhiteSpace(dto.Title) ? "Unknown Offer" : dto.Title,
            Description: dto.Description ?? "",
            Type: productType,
            Price: dto.Price,
            Currency: string.IsNullOrWhiteSpace(dto.Currency) ? "EUR" : dto.Currency,
            IsActive: dto.IsActive,
            Metadata: metadata,
            ImageUrl: resolvedImageUrl
        );
    }

    private static PaymentProviderDto MapToDomainProvider(PaymentProviderJsonDto dto)
        => new(
            Id: string.IsNullOrWhiteSpace(dto.Id) ? "MockProvider" : dto.Id,
            DisplayName: dto.DisplayName ?? dto.Id ?? "Payment Gateway",
            IsEnabled: dto.IsEnabled,
            IsSandbox: dto.IsSandbox,
            IconUrl: dto.IconUrl ?? "/images/providers/default.svg"
        );

    private static string BuildKey(string region, string store, string gameVersion)
        => $"{region.Trim().ToLowerInvariant()}:{store.Trim().ToLowerInvariant()}:{gameVersion.Trim().ToLowerInvariant()}";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

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
