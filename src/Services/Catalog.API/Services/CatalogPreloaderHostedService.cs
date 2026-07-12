using Catalog.API.Services;

namespace Catalog.API.Services;

public sealed class CatalogPreloaderHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<CatalogPreloaderHostedService> _logger;

    public CatalogPreloaderHostedService(IServiceProvider services, ILogger<CatalogPreloaderHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<Configuration.SupabaseOptions>();
            var provider = scope.ServiceProvider.GetRequiredService<ICatalogProvider>();
            // Warm the Global config — the one anonymous visitors land on.
            await provider.GetOrFetchAsync(options.DefaultRegion, options.DefaultStore, options.DefaultGameVersion, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось выполнить предзагрузку каталога по умолчанию.");
        }
    }
}
