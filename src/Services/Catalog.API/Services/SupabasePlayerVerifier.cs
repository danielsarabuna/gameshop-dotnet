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
    string? ErrorMessage = null,
    string? PlayerName = null
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
                var playerName = await GetPlayerNameAsync(userId, cancellationToken);

                return new PlayerContextResult(true, userId, region, store, version, PlayerName: playerName);
            }

            return new PlayerContextResult(false, "", "", "", "", "Invalid ticket response structure.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while claiming a web-shop ticket");
            return new PlayerContextResult(false, "", "", "", "", "Ticket validation failed on Supabase server.");
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception verifying player ID {UserId}", rawUserId);
            if (Guid.TryParse(trimmedId, out _))
            {
                return new PlayerContextResult(true, trimmedId, _options.DefaultRegion, _options.DefaultStore, _options.DefaultGameVersion);
            }
            return new PlayerContextResult(false, "", "", "", "", "Player verification failed on Supabase server.");
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

    private async Task<string?> GetPlayerNameAsync(string userId, CancellationToken cancellationToken)
    {
        var characterName = await GetCurrentCharacterNameAsync(userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(characterName))
        {
            // DEBUG-SESSION: remove after the Unity deeplink confirms the active character name.
            _logger.LogInformation("DEBUG-SESSION WebShop player name resolved from active character state for {UserId}", userId);
            return characterName;
        }

        var profileName = await GetProfileNameAsync(userId, cancellationToken);
        // DEBUG-SESSION: remove after the Unity deeplink confirms the active character name.
        _logger.LogInformation(
            "DEBUG-SESSION WebShop player name fallback for {UserId}: {Source}",
            userId,
            string.IsNullOrWhiteSpace(profileName) ? "none" : "user_profile");
        return profileName;
    }

    private async Task<string?> GetCurrentCharacterNameAsync(string userId, CancellationToken cancellationToken)
    {
        var url = $"{_options.Url.TrimEnd('/')}/rest/v1/user_character_state?user_id=eq.{Uri.EscapeDataString(userId)}&select=data&limit=1";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddSupabaseHeaders(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (rows.RootElement.ValueKind != JsonValueKind.Array || rows.RootElement.GetArrayLength() == 0)
                return null;

            var data = rows.RootElement[0].GetProperty("data");
            using var state = data.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(data.GetString() ?? "{}")
                : JsonDocument.Parse(data.GetRawText());

            if (!TryGetString(state.RootElement, "currentState", out var currentStateId)
                || !TryGetProperty(state.RootElement, "characters", out var characters)
                || characters.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var character in characters.EnumerateArray())
            {
                if (!TryGetProperty(character, "Id", out var idElement)
                    || !string.Equals(ReadEntityId(idElement), currentStateId, StringComparison.OrdinalIgnoreCase)
                    || !TryGetProperty(character, "meta", out var meta)
                    || !TryGetString(meta, "name", out var name))
                {
                    continue;
                }

                return name;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read active character name for player {UserId}", userId);
        }

        return null;
    }

    private async Task<string?> GetProfileNameAsync(string userId, CancellationToken cancellationToken)
    {
        var url = $"{_options.Url.TrimEnd('/')}/rest/v1/user_profile?user_id=eq.{Uri.EscapeDataString(userId)}&select=data&limit=1";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddSupabaseHeaders(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (rows.RootElement.ValueKind != JsonValueKind.Array || rows.RootElement.GetArrayLength() == 0)
                return null;

            var data = rows.RootElement[0].GetProperty("data");
            using var profile = data.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(data.GetString() ?? "{}")
                : JsonDocument.Parse(data.GetRawText());

            foreach (var key in new[] { "Nickname", "nickname", "DisplayName", "displayName", "Username", "username" })
            {
                if (profile.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var name = value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read display name for player {UserId}", userId);
        }

        return null;
    }

    private static string? ReadEntityId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString();

        return TryGetString(element, "Value", out var value) ? value : null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = "";
        if (!TryGetProperty(element, propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString()?.Trim() ?? "";
        return value.Length > 0;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
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
