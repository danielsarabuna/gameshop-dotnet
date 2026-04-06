namespace Basket.API.Storage;

public sealed class RedisBasketOptions
{
    public string? ConnectionString { get; set; }
    public string? KeyPrefix { get; set; }
    public int? TtlMinutes { get; set; }
    public bool? SlidingTtl { get; set; }
}
