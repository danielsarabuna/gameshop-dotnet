using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;

namespace Ordering.Infrastructure.Integrations;

/// <summary>
/// Checks the player id against Supabase auth (auth.users) using the service role key.
/// Registered only when Supabase credentials are configured — otherwise the dev
/// AllowAllIdentityVerifier takes over, so local InMemory flows stay frictionless.
/// </summary>
public sealed class SupabaseIdentityVerifier : IPlayerIdentityVerifier
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _serviceKey;
    private readonly ILogger<SupabaseIdentityVerifier> _logger;

    public SupabaseIdentityVerifier(HttpClient http, IConfiguration configuration, ILogger<SupabaseIdentityVerifier> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = (configuration["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "").TrimEnd('/');
        _serviceKey = configuration["Supabase:ServiceRoleKey"] ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY") ?? "";
    }

    public bool IsConfigured => _baseUrl.Length > 0 && _serviceKey.Length > 0;

    public async Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken)
    {
        // auth.users ids are UUIDs; anything else cannot be a valid game account.
        if (!Guid.TryParse(userId, out _))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/auth/v1/admin/users/{userId}");
            request.Headers.Add("apikey", _serviceKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _serviceKey);

            using var response = await _http.SendAsync(request, cancellationToken);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Fail-closed: unreachable identity backend must not become a spoofing window.
            _logger.LogWarning(ex, "Supabase identity lookup failed for {UserId}; rejecting.", userId);
            return false;
        }
    }
}
