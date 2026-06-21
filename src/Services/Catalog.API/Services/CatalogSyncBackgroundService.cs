using Catalog.API.Configuration;

namespace Catalog.API.Services;

public sealed class CatalogSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RemoteCatalogOptions _options;
    private readonly ILogger<CatalogSyncBackgroundService> _logger;

    public CatalogSyncBackgroundService(
        IServiceProvider serviceProvider,
        RemoteCatalogOptions options,
        ILogger<CatalogSyncBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnablePolling)
        {
            _logger.LogInformation("Автоматический поллинг конфигов каталога отключён в конфигурации.");
            return;
        }

        _logger.LogInformation("Запущен фоновый сервис синхронизации каталогов WebShop с Supabase Storage (интервал: {Interval}с)...",
            _options.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<ICatalogSyncService>();

                await syncService.SyncAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Ошибка во время фоновой синхронизации каталогов");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds)), stoppingToken);
        }
    }
}
