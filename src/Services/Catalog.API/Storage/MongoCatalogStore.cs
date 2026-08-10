using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Collections.Concurrent;

namespace Catalog.API.Storage;

public sealed class MongoCatalogStore : ICatalogStore
{
    private readonly IMongoCollection<CatalogItemDocument> _collection;
    private readonly ILogger<MongoCatalogStore> _logger;
    private readonly ConcurrentDictionary<string, WebShopCatalogConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    public MongoCatalogStore(IConfiguration configuration, IHostEnvironment environment, ILogger<MongoCatalogStore> logger)
    {
        _logger = logger;
        var options = configuration.GetSection("Catalog:Mongo").Get<MongoCatalogOptions>() ?? new MongoCatalogOptions();
        var defaultConnection = environment.IsEnvironment("Docker")
            ? "mongodb://mongodb:27017"
            : "mongodb://localhost:27017";

        var connectionString = string.IsNullOrWhiteSpace(options.ConnectionString)
            ? defaultConnection
            : options.ConnectionString;
        var databaseName = string.IsNullOrWhiteSpace(options.Database) ? "webshop_catalog" : options.Database;
        var collectionName = string.IsNullOrWhiteSpace(options.Collection) ? "items" : options.Collection;
        var seedOnStart = options.SeedOnStart ?? true;

        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        _collection = database.GetCollection<CatalogItemDocument>(collectionName);

        if (seedOnStart)
        {
            EnsureSeeded();
        }
    }

    public void UpdateCatalogConfig(string region, string store, string gameVersion, WebShopCatalogConfig config)
    {
        var key = $"{region.Trim().ToLowerInvariant()}:{store.Trim().ToLowerInvariant()}:{gameVersion.Trim().ToLowerInvariant()}";
        _configs[key] = config;
    }

    public IReadOnlyList<CatalogItem> GetAll()
    {
        var documents = _collection.Find(FilterDefinition<CatalogItemDocument>.Empty).ToList();
        return documents.Select(MapToModel).ToList();
    }

    public IReadOnlyList<CatalogItem> GetFiltered(string? region, string? store, string? gameVersion)
    {
        var key = $"{region?.Trim().ToLowerInvariant()}:{store?.Trim().ToLowerInvariant()}:{gameVersion?.Trim().ToLowerInvariant()}";
        if (_configs.TryGetValue(key, out var config))
        {
            return config.Items;
        }

        return GetAll();
    }

    public CatalogItem? GetById(Guid id)
    {
        var document = _collection.Find(item => item.Id == id).FirstOrDefault();
        return document is null ? null : MapToModel(document);
    }

    public PaymentProviderConfig GetPaymentProviders(string? region, string? store, string? gameVersion)
    {
        var key = $"{region?.Trim().ToLowerInvariant()}:{store?.Trim().ToLowerInvariant()}:{gameVersion?.Trim().ToLowerInvariant()}";
        if (_configs.TryGetValue(key, out var config))
        {
            return new PaymentProviderConfig(config.PaymentProviders);
        }

        return new PaymentProviderConfig(new List<PaymentProviderDto>
        {
            new("YooKassa", "ЮKassa", true, false, "/images/providers/yookassa.svg"),
            new("MockProvider", "Тестовая оплата", true, true, "/images/providers/mock.svg")
        });
    }

    private void EnsureSeeded()
    {
        if (_collection.EstimatedDocumentCount() > 0)
        {
            return;
        }

        var documents = CatalogSeedData.Items.Select(MapToDocument).ToList();
        if (documents.Count == 0)
        {
            return;
        }

        _collection.InsertMany(documents);
        _logger.LogInformation("Catalog seeded with {Count} items.", documents.Count);
    }

    private static CatalogItemDocument MapToDocument(CatalogItem item)
    {
        return new CatalogItemDocument
        {
            Id = item.Id,
            Title = item.Title,
            Description = item.Description,
            Type = item.Type,
            Price = item.Price,
            Currency = item.Currency,
            IsActive = item.IsActive,
            Metadata = item.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value),
            ImageUrl = item.ImageUrl
        };
    }

    private static CatalogItem MapToModel(CatalogItemDocument document)
    {
        return new CatalogItem(
            document.Id,
            document.Title,
            document.Description,
            document.Type,
            document.Price,
            document.Currency,
            document.IsActive,
            document.Metadata,
            document.ImageUrl
        );
    }

    private sealed class CatalogItemDocument
    {
        [BsonId]
        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CatalogProductType Type { get; set; }
        public decimal Price { get; set; }
        public string Currency { get; set; } = "EUR";
        public bool IsActive { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public string? ImageUrl { get; set; }
    }
}
