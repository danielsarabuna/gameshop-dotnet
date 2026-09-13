using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Catalog.API.Services;

public sealed class CatalogCacheInvalidationAuthenticator
{
    public const string TimestampHeader = "X-Catalog-Timestamp";
    public const string SignatureHeader = "X-Catalog-Signature";

    private readonly string _secret;
    private readonly TimeSpan _maxAge;
    private readonly ConcurrentDictionary<string, long> _processedSignatures = new(StringComparer.Ordinal);

    public CatalogCacheInvalidationAuthenticator(IConfiguration configuration)
    {
        _secret = configuration["Catalog:CacheInvalidation:SharedSecret"] ?? "";
        _maxAge = TimeSpan.FromSeconds(configuration.GetValue("Catalog:CacheInvalidation:MaxAgeSeconds", 300));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_secret);

    public bool TryValidate(string? timestampHeader, string? signatureHeader, string body, DateTimeOffset now, out bool isReplay)
    {
        isReplay = false;
        if (!IsConfigured
            || !long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        DateTimeOffset sentAt;
        try
        {
            sentAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if (now - sentAt > _maxAge || sentAt - now > _maxAge)
        {
            return false;
        }

        byte[] suppliedSignature;
        try
        {
            suppliedSignature = Convert.FromHexString(signatureHeader);
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        var expectedSignature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_secret), payload);
        if (suppliedSignature.Length != expectedSignature.Length
            || !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
        {
            return false;
        }

        foreach (var item in _processedSignatures)
        {
            if (item.Value <= now.ToUnixTimeSeconds() - (long)_maxAge.TotalSeconds)
            {
                _processedSignatures.TryRemove(item.Key, out _);
            }
        }

        isReplay = !_processedSignatures.TryAdd(signatureHeader, timestamp);
        return true;
    }
}
