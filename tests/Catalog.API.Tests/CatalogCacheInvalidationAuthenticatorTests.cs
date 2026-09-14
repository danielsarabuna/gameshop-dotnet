using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Catalog.API.Services;
using Microsoft.Extensions.Configuration;

namespace Catalog.API.Tests;

public sealed class CatalogCacheInvalidationAuthenticatorTests
{
    private const string Secret = "test-secret";
    private const string Body = "{\"region\":\"russia\",\"store\":\"ru_store\",\"gameVersion\":\"0.0.36\"}";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void TryValidate_AcceptsCorrectSignature()
    {
        var authenticator = CreateAuthenticator();
        var timestamp = Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        var valid = authenticator.TryValidate(timestamp, Sign(timestamp, Body), Body, Now, out var isReplay);

        Assert.True(valid);
        Assert.False(isReplay);
    }

    [Fact]
    public void TryValidate_RejectsInvalidSignatureAndExpiredTimestamp()
    {
        var authenticator = CreateAuthenticator();
        var timestamp = Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        Assert.False(authenticator.TryValidate(timestamp, new string('0', 64), Body, Now, out _));

        var expired = (Now - TimeSpan.FromMinutes(6)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        Assert.False(authenticator.TryValidate(expired, Sign(expired, Body), Body, Now, out _));
    }

    [Fact]
    public void TryValidate_MarksRepeatedEventAsReplay()
    {
        var authenticator = CreateAuthenticator();
        var timestamp = Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = Sign(timestamp, Body);

        Assert.True(authenticator.TryValidate(timestamp, signature, Body, Now, out var firstReplay));
        Assert.False(firstReplay);
        Assert.True(authenticator.TryValidate(timestamp, signature, Body, Now, out var secondReplay));
        Assert.True(secondReplay);
    }

    [Fact]
    public void WebhookPayload_UsesDocumentedCamelCaseFields()
    {
        var payload = JsonSerializer.Deserialize<CatalogCacheInvalidationRequest>(Body);

        Assert.NotNull(payload);
        Assert.Equal("russia", payload!.Region);
        Assert.Equal("ru_store", payload.Store);
        Assert.Equal("0.0.36", payload.GameVersion);
    }

    private static CatalogCacheInvalidationAuthenticator CreateAuthenticator() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Catalog:CacheInvalidation:SharedSecret"] = Secret,
            ["Catalog:CacheInvalidation:MaxAgeSeconds"] = "300"
        }).Build());

    private static string Sign(string timestamp, string body) => Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
}
