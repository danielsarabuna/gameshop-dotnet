using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Catalog.API.Configuration;

namespace Catalog.API.Services;

public sealed record PlayerContextResult(
    bool IsValid,
    string UserId,
    string Region,
    string Store,
    string GameVersion,
    string? ErrorMessage = null
);

public interface ISupabasePlayerVerifier
{
    Task<PlayerContextResult> ClaimTicketAsync(Guid ticketId, CancellationToken cancellationToken);
    Task<PlayerContextResult> VerifyDirectPlayerIdAsync(string rawUserId, CancellationToken cancellationToken);
}

public sealed class SupabasePlayerVerifier : ISupabasePlayerVerifier
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseOptions _options;
    private readonly ILogger<SupabasePlayerVerifier> _logger;

    public SupabasePlayerVerifier(
        HttpClient httpClient,
        SupabaseOptions options,
        ILogger<SupabasePlayerVerifier> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<PlayerContextResult> ClaimTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var rpcUrl = $"{_options.Url.TrimEnd('/')}/rest/v1/rpc/consume_webshop_ticket";
        var payload = JsonSerializer.Serialize(new { p_ticket_id = ticketId });

        try
        {
            using var request = BuildSupabaseRequest(rpcUrl, payload);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Supabase consume_webshop_ticket RPC failed ({Status}): {Error}", response.StatusCode, errorText);
                return new PlayerContextResult(false, "", "", "", "", "Ticket validation failed on Supabase server.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var item = doc.RootElement[0];
                var isValid = item.TryGetProperty("is_valid", out var validProp) && validProp.GetBoolean();
                if (!isValid)
                {
                    return new PlayerContextResult(false, "", "", "", "", "Invalid or expired ticket.");
                }

                var userId = item.GetProperty("user_id").GetString() ?? "";
                var region = item.GetProperty("region").GetString() ?? _options.DefaultRegion;
                var store = item.GetProperty("store_channel").GetString() ?? _options.DefaultStore;
                var version = item.GetProperty("game_version").GetString() ?? _options.DefaultGameVersion;

                return new PlayerContextResult(true, userId, region, store, version);
            }

            return new PlayerContextResult(false, "", "", "", "", "Invalid ticket response structure.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while claiming ticket {TicketId}", ticketId);
            return new PlayerContextResult(false, "", "", "", "", ex.Message);
        }
    }

    public async Task<PlayerContextResult> VerifyDirectPlayerIdAsync(string rawUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawUserId))
        {
            return new PlayerContextResult(false, "", "", "", "", "Player ID cannot be empty.");
        }

        var trimmedId = rawUserId.Trim();
        var queryUrl = $"{_options.Url.TrimEnd('/')}/rest/v1/user_profile?user_id=eq.{Uri.EscapeDataString(trimmedId)}&select=user_id,updated_at";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
            AddSupabaseHeaders(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ValidateFallbackPlayerId(trimmedId);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var verifiedId = doc.RootElement[0].GetProperty("user_id").GetString() ?? trimmedId;
                return new PlayerContextResult(true, verifiedId, _options.DefaultRegion, _options.DefaultStore, _options.DefaultGameVersion);
            }

            if (Guid.TryParse(trimmedId, out _))
            {
                return new PlayerContextResult(true, trimmedId, _options.DefaultRegion, _options.DefaultStore, _options.DefaultGameVersion);
            }

            return new PlayerContextResult(false, "", "", "", "", "Player ID not found in game database.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception verifying player ID {UserId}", rawUserId);
            if (Guid.TryParse(trimmedId, out _))
            {
                return new PlayerContextResult(true, trimmedId, _options.DefaultRegion, _options.DefaultStore, _options.DefaultGameVersion);
            }
            return new PlayerContextResult(false, "", "", "", "", "Error verifying player ID: " + ex.Message);
        }
    }

    private PlayerContextResult ValidateFallbackPlayerId(string trimmedId)
    {
        if (Guid.TryParse(trimmedId, out _))
        {
            return new PlayerContextResult(true, trimmedId, _options.DefaultRegion, _options.DefaultStore, _options.DefaultGameVersion);
        }
        return new PlayerContextResult(false, "", "", "", "", "Invalid player ID format.");
    }

    private HttpRequestMessage BuildSupabaseRequest(string url, string jsonBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        AddSupabaseHeaders(req);
        return req;
    }

    private void AddSupabaseHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_options.ServiceRoleKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
            request.Headers.Add("apikey", _options.ServiceRoleKey);
        }
    }
}
