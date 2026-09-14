using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Basket.API.Storage;

public sealed class RedisBasketStore : IBasketStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly TimeSpan? _ttl;
    private readonly bool _slidingTtl;
    private readonly ILogger<RedisBasketStore> _logger;

    public RedisBasketStore(IConfiguration configuration, IHostEnvironment environment, ILogger<RedisBasketStore> logger)
    {
        _logger = logger;
        var options = configuration.GetSection("Basket:Redis").Get<RedisBasketOptions>() ?? new RedisBasketOptions();
        var defaultConnection = environment.IsEnvironment("Docker")
            ? "redis:6379"
            : "localhost:6379";

        var connectionString = string.IsNullOrWhiteSpace(options.ConnectionString)
            ? defaultConnection
            : options.ConnectionString;

        _keyPrefix = string.IsNullOrWhiteSpace(options.KeyPrefix) ? "basket:" : options.KeyPrefix;

        var ttlMinutes = options.TtlMinutes ?? 60;
        _ttl = ttlMinutes > 0 ? TimeSpan.FromMinutes(ttlMinutes) : null;
        _slidingTtl = options.SlidingTtl ?? true;

        var connection = ConnectionMultiplexer.Connect(connectionString);
        _database = connection.GetDatabase();
    }

    public Basket? Get(string userId)
    {
        var key = BuildKey(userId);
        var value = _database.StringGet(key);
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        var basket = JsonSerializer.Deserialize<Basket>(value.ToString(), JsonOptions);
        if (basket is null)
        {
            _logger.LogWarning("Failed to deserialize basket for user {UserId}.", userId);
            return null;
        }

        if (_slidingTtl && _ttl.HasValue)
        {
            _database.KeyExpire(key, _ttl);
        }

        return basket;
    }

    public void Upsert(Basket basket)
    {
        var key = BuildKey(basket.UserId);
        var payload = JsonSerializer.Serialize(basket, JsonOptions);
        if (_ttl.HasValue)
        {
            _database.StringSet(key, payload, _ttl.Value);
        }
        else
        {
            _database.StringSet(key, payload);
        }
    }

    public void Delete(string userId)
    {
        var key = BuildKey(userId);
        _database.KeyDelete(key);
    }

    private string BuildKey(string userId) => $"{_keyPrefix}{userId}";
}
