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
            var provider = scope.ServiceProvider.GetRequiredService<ICatalogProvider>();
            await provider.GetOrFetchAsync("russia", "ru_store", "0.0.36", stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось выполнить предзагрузку каталога по умолчанию.");
        }
    }
}
