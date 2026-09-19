using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Catalog.API.Configuration;
using Catalog.API.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.API.Services;

public interface ICatalogProvider
{
    /// <summary>
    /// Returns the latest catalog config published for (region, store). The client gameVersion
    /// is deliberately ignored so an old link cannot expose an old price.
    /// </summary>
    Task<WebShopCatalogConfig?> GetOrFetchAsync(string region, string store, string gameVersion, CancellationToken ct);

    /// <summary>Invalidates the in-memory latest config for the region/store.</summary>
    Task<bool> InvalidateAsync(string region, string store, string gameVersion, CancellationToken ct);

    /// <summary>Ensures the specified relative asset is downloaded from Supabase to disk cache.</summary>
    Task<bool> EnsureAssetDownloadedAsync(string region, string store, string gameVersion, string relativePath, CancellationToken ct);

    /// <summary>Opens a validated cached asset, downloading it first when needed.</summary>
    Task<CatalogAssetStream?> OpenAssetAsync(string region, string store, string gameVersion, string relativePath, CancellationToken ct);
}

public sealed record CatalogAssetStream(Stream Content, DateTimeOffset LastModified);

public sealed class CatalogProvider : ICatalogProvider
{
    private readonly HttpClient _http;
    private readonly ICatalogStore _store;
    private readonly RemoteCatalogOptions _options;
    private readonly SupabaseOptions _supabaseOptions;
    private readonly ILogger<CatalogProvider> _logger;

    private const string LatestVersionCacheKey = "__latest__";

    // Guards against duplicate concurrent fetches for the same key.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    [ActivatorUtilitiesConstructor]
    public CatalogProvider(
        HttpClient http,
        ICatalogStore store,
        RemoteCatalogOptions options,
        SupabaseOptions supabaseOptions,
        ILogger<CatalogProvider> logger)
    {
        _http = http;
        _store = store;
        _options = options;
        _supabaseOptions = supabaseOptions;
        _logger = logger;
    }

    // Kept for focused tests that do not need Storage listing credentials.
    public CatalogProvider(HttpClient http, ICatalogStore store, RemoteCatalogOptions options, ILogger<CatalogProvider> logger)
        : this(http, store, options, new SupabaseOptions(), logger)
    {
    }

    public async Task<WebShopCatalogConfig?> GetOrFetchAsync(string region, string store, string gameVersion, CancellationToken ct)
    {
        var key = BuildLatestKey(region, store);

        var cached = _store.GetConfig(region, store, LatestVersionCacheKey);
        if (cached is not null)
        {
            return cached;
        }

        return await FetchUnderLockAsync(region, store, key, ct);
    }

    public async Task<bool> InvalidateAsync(string region, string store, string gameVersion, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(BuildLatestKey(region, store), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return _store.RemoveConfig(region, store, LatestVersionCacheKey);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<WebShopCatalogConfig?> FetchUnderLockAsync(string region, string store, string key, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Re-check after acquiring the lock (another caller may have just fetched).
            var cached = _store.GetConfig(region, store, LatestVersionCacheKey);
            if (cached is not null)
            {
                return cached;
            }

            var latestVersion = await FindLatestPublishedVersionAsync(region, store, ct);
            return latestVersion is null ? null : await FetchFromSupabaseAsync(region, store, latestVersion, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<WebShopCatalogConfig?> FetchFromSupabaseAsync(string region, string store, string gameVersion, CancellationToken ct)
    {
        var fileName = $"webshop_config_{gameVersion}.json";
        var url = $"{_options.SupabaseBaseUrl.TrimEnd('/')}/storage/v1/object/public/{_options.Bucket}/{region}/{store}/{fileName}";

        try
        {
            using var response = await _http.GetAsync(url, ct);
            // Supabase returns 400 (not 404) for missing public objects — treat both as absent.
            if (response.StatusCode == HttpStatusCode.NotFound || (int)response.StatusCode == 400)
            {
                _logger.LogWarning("Конфиг каталога не найден.");
                return null;
            }

            response.EnsureSuccessStatusCode();

            var jsonText = await response.Content.ReadAsStringAsync(ct);
            var dto = JsonSerializer.Deserialize<WebShopCatalogJsonDto>(jsonText, JsonOpts);

            if (!IsValidCatalog(dto))
            {
                _logger.LogWarning("Конфиг каталога невалиден.");
                return null;
            }

            var domainItems = await BuildDomainItemsAsync(dto!, region, store, gameVersion, ct);
            var providers = dto!.PaymentProviders?.Select(MapToDomainProvider).ToList() ?? new List<PaymentProviderDto>();
            var config = new WebShopCatalogConfig(
                GameVersion: gameVersion,
                Environment: dto.Environment ?? _options.Bucket,
                Region: dto.Region ?? region,
                Store: dto.Store ?? store,
                UpdatedAt: dto.UpdatedAt ?? DateTime.UtcNow.ToString("o"),
                PaymentProviders: providers,
                Items: domainItems);

            _store.SetConfig(region, store, LatestVersionCacheKey, config);
            _logger.LogInformation(
                "Каталог загружен с Supabase: {ItemCount} офферов, {ProviderCount} провайдеров.",
                domainItems.Count, providers.Count);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось загрузить каталог.");
        }

        return null;
    }

    private async Task<string?> FindLatestPublishedVersionAsync(string region, string store, CancellationToken ct)
    {
        var url = $"{_options.SupabaseBaseUrl.TrimEnd('/')}/storage/v1/object/list/{Uri.EscapeDataString(_options.Bucket)}";
        var prefix = $"{region.Trim('/')}/{store.Trim('/')}";
        var versions = new List<(Version Parsed, string Text)>();

        for (var offset = 0; ; offset += 100)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new StorageListRequest(prefix, 100, offset))
            };
            AddStorageCredentials(request);

            try
            {
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Не удалось получить список конфигов каталога: HTTP {StatusCode}.", (int)response.StatusCode);
                    return null;
                }

                var page = await response.Content.ReadFromJsonAsync<List<StorageObjectJsonDto>>(JsonOpts, ct) ?? [];
                foreach (var entry in page)
                {
                    if (TryParseConfigVersion(entry.Name, out var version, out var versionText))
                    {
                        versions.Add((version, versionText));
                    }
                }

                if (page.Count < 100)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось получить список конфигов каталога.");
                return null;
            }
        }

        return versions.Count == 0 ? null : versions.MaxBy(candidate => candidate.Parsed).Text;
    }

    private void AddStorageCredentials(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_supabaseOptions.ServiceRoleKey))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseOptions.ServiceRoleKey);
        request.Headers.TryAddWithoutValidation("apikey", _supabaseOptions.ServiceRoleKey);
    }

    private static bool TryParseConfigVersion(string? name, out Version version, out string versionText)
    {
        const string prefix = "webshop_config_";
        const string suffix = ".json";
        version = new Version();
        versionText = string.Empty;

        if (string.IsNullOrWhiteSpace(name)
            || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawVersion = name[prefix.Length..^suffix.Length];
        if (!Version.TryParse(rawVersion, out var parsedVersion) || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        versionText = rawVersion;
        return true;
    }

    /// <summary>
    /// Structural validation of a fetched remote config (see docs/webshop-config.schema.json).
    /// Invalid configs are treated as absent so a broken deploy can never poison the shop.
    /// </summary>
    private static bool IsValidCatalog(WebShopCatalogJsonDto? dto)
    {
        if (dto is not { Items: { Count: > 0 } })
        {
            return false;
        }

        foreach (var item in dto.Items)
        {
            if (!Guid.TryParse(item.Id, out _) || (string.IsNullOrWhiteSpace(item.Title) && !HasLocalizedTitle(item)))
            {
                return false;
            }

            if (item.Price < 0m)
            {
                return false;
            }

            if (string.IsNullOrEmpty(item.Currency) || item.Currency.Length != 3)
            {
                return false;
            }

            if (!Enum.TryParse<CatalogProductType>(item.Type, ignoreCase: true, out _))
            {
                return false;
            }
        }

        if (dto.PaymentProviders is not null)
        {
            foreach (var provider in dto.PaymentProviders)
            {
                if (string.IsNullOrWhiteSpace(provider.Id))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasLocalizedTitle(CatalogItemJsonDto item)
        => item.Locales?.Values.Any(locale => !string.IsNullOrWhiteSpace(locale.Title)) == true;

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

            items.Add(MapToDomainItem(item, region, store, resolvedUrl, dto.DefaultLocale));
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

        if (!CatalogAssetPath.TryResolve(_options.AssetCacheDir, region, store, gameVersion, relativePath, out var resolved))
        {
            _logger.LogWarning("Отклонён небезопасный путь ассета.");
            return false;
        }

        if (File.Exists(resolved))
        {
            return true; // already cached on disk
        }

        var cleanRel = string.Join('/', relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var sourceUrl = $"{_options.SupabaseBaseUrl.TrimEnd('/')}/storage/v1/object/public/{_options.Bucket}/{region}/{store}/{cleanRel}";
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);

        try
        {
            var bytes = await _http.GetByteArrayAsync(sourceUrl, ct);
            await File.WriteAllBytesAsync(resolved, bytes, ct);
            _logger.LogDebug("Ассет закэширован: {Bytes} байт", bytes.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось скачать ассет.");
            return false;
        }
    }

    public async Task<CatalogAssetStream?> OpenAssetAsync(string region, string store, string gameVersion, string relativePath, CancellationToken ct)
    {
        if (!CatalogAssetPath.TryResolve(_options.AssetCacheDir, region, store, gameVersion, relativePath, out var resolved))
        {
            return null;
        }

        if (!File.Exists(resolved)
            && !await EnsureAssetDownloadedAsync(region, store, gameVersion, relativePath, ct))
        {
            return null;
        }

        if (!File.Exists(resolved))
        {
            return null;
        }

        return new CatalogAssetStream(File.OpenRead(resolved), new DateTimeOffset(File.GetLastWriteTimeUtc(resolved)));
    }

    private static CatalogItem MapToDomainItem(CatalogItemJsonDto dto, string region, string store, string? resolvedImageUrl, string? defaultLocale)
    {
        Enum.TryParse<CatalogProductType>(dto.Type, true, out var productType);
        var metadata = dto.Metadata ?? new Dictionary<string, string>();
        var localizations = dto.Locales?
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Title))
            .ToDictionary(
                pair => pair.Key,
                pair => new CatalogItemLocalization(pair.Value.Title!, pair.Value.Description ?? ""),
                StringComparer.OrdinalIgnoreCase);
        var defaultLocalization = FindLocalization(localizations, defaultLocale);
        return new CatalogItem(
            Id: Guid.Parse(dto.Id!),
            Title: dto.Title ?? defaultLocalization?.Title ?? "Unknown Offer",
            Description: dto.Description ?? defaultLocalization?.Description ?? "",
            Type: productType,
            Price: dto.Price,
            Currency: string.IsNullOrWhiteSpace(dto.Currency) ? "EUR" : dto.Currency,
            IsActive: dto.IsActive,
            Metadata: metadata,
            ImageUrl: resolvedImageUrl,
            Localizations: localizations
        );
    }

    private static CatalogItemLocalization? FindLocalization(IReadOnlyDictionary<string, CatalogItemLocalization>? localizations, string? locale)
    {
        if (localizations is null || localizations.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(locale) && localizations.TryGetValue(locale, out var exact))
        {
            return exact;
        }

        return localizations.TryGetValue("en-US", out var english) ? english : localizations.Values.First();
    }

    private static PaymentProviderDto MapToDomainProvider(PaymentProviderJsonDto dto)
        => new(
            Id: string.IsNullOrWhiteSpace(dto.Id) ? "MockProvider" : dto.Id,
            DisplayName: dto.DisplayName ?? dto.Id ?? "Payment Gateway",
            IsEnabled: dto.IsEnabled,
            IsSandbox: dto.IsSandbox,
            IconUrl: dto.IconUrl ?? "/images/providers/default.svg"
        );

    private static string BuildLatestKey(string region, string store)
        => $"{region.Trim().ToLowerInvariant()}:{store.Trim().ToLowerInvariant()}:latest";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class WebShopCatalogJsonDto
    {
        public string? GameVersion { get; set; }
        public string? Environment { get; set; }
        public string? Region { get; set; }
        public string? Store { get; set; }
        public string? UpdatedAt { get; set; }
        public string? DefaultLocale { get; set; }
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
        public Dictionary<string, CatalogItemLocaleJsonDto>? Locales { get; set; }
    }

    private sealed class CatalogItemLocaleJsonDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
    }

    private sealed record StorageListRequest(string Prefix, int Limit, int Offset)
    {
        public object SortBy { get; } = new { column = "name", order = "asc" };
    }

    private sealed class StorageObjectJsonDto
    {
        public string? Name { get; set; }
    }
}
