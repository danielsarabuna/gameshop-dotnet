using System.Net.Http.Headers;
using System.Net;
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
    string? PlayerName = null,
    string? ErrorCode = null,
    int DeliveryContractVersion = 1
);

public interface ISupabasePlayerVerifier
{
    Task<PlayerContextResult> ClaimTicketAsync(Guid ticketId, CancellationToken cancellationToken);
    Task<PlayerContextResult> ResolvePlayerAsync(string rawUserId, CancellationToken cancellationToken);
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
                var deliveryVersion = item.TryGetProperty("delivery_contract_version", out var deliveryVersionProp)
                    ? Math.Max(1, deliveryVersionProp.GetInt32())
                    : 1;
                var playerName = await GetPlayerNameAsync(userId, cancellationToken);

                return new PlayerContextResult(true, userId, region, store, version, PlayerName: playerName, DeliveryContractVersion: deliveryVersion);
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

    public async Task<PlayerContextResult> ResolvePlayerAsync(string rawUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawUserId))
        {
            return new PlayerContextResult(false, "", "", "", "", "Player ID is required.", ErrorCode: "invalid_player");
        }

        var trimmedId = rawUserId.Trim();
        if (!Guid.TryParse(trimmedId, out _))
        {
            return new PlayerContextResult(false, "", "", "", "", "Player could not be resolved.", ErrorCode: "invalid_player");
        }

        var queryUrl = $"{_options.Url.TrimEnd('/')}/rest/v1/webshop_player_context?user_id=eq.{Uri.EscapeDataString(trimmedId)}&select=user_id,region,store_channel,game_version,delivery_contract_version&limit=1";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, queryUrl);
            AddSupabaseHeaders(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound
                    || error.Contains("PGRST205", StringComparison.Ordinal)
                    || error.Contains("delivery_contract_version", StringComparison.Ordinal))
                {
                    return await ResolveFromTicketHistoryAsync(trimmedId, cancellationToken);
                }

                return new PlayerContextResult(false, "", "", "", "", "Player lookup is temporarily unavailable.", ErrorCode: "lookup_unavailable");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var item = doc.RootElement[0];
                var verifiedId = item.GetProperty("user_id").GetString() ?? trimmedId;
                var region = item.GetProperty("region").GetString() ?? _options.DefaultRegion;
                var store = item.GetProperty("store_channel").GetString() ?? _options.DefaultStore;
                var version = item.GetProperty("game_version").GetString() ?? _options.DefaultGameVersion;
                var deliveryVersion = item.TryGetProperty("delivery_contract_version", out var deliveryVersionProp)
                    ? Math.Max(1, deliveryVersionProp.GetInt32())
                    : 1;
                var playerName = await GetPlayerNameAsync(verifiedId, cancellationToken);
                return new PlayerContextResult(true, verifiedId, region, store, version, PlayerName: playerName, DeliveryContractVersion: deliveryVersion);
            }

            return new PlayerContextResult(
                false,
                "",
                "",
                "",
                "",
                "Open the game once to synchronize this player with the shop.",
                ErrorCode: "context_missing");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while resolving a WebShop player");
            return new PlayerContextResult(false, "", "", "", "", "Player lookup is temporarily unavailable.", ErrorCode: "lookup_unavailable");
        }
    }

    private async Task<PlayerContextResult> ResolveFromTicketHistoryAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var url = $"{_options.Url.TrimEnd('/')}/rest/v1/webshop_tickets?user_id=eq.{Uri.EscapeDataString(userId)}&select=user_id,region,store_channel,game_version&order=created_at.desc&limit=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddSupabaseHeaders(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new PlayerContextResult(false, "", "", "", "", "Player lookup is temporarily unavailable.", ErrorCode: "lookup_unavailable");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return new PlayerContextResult(false, "", "", "", "", "Open the game once to synchronize this player with the shop.", ErrorCode: "context_missing");
        }

        var item = doc.RootElement[0];
        var verifiedId = item.GetProperty("user_id").GetString() ?? userId;
        var region = item.GetProperty("region").GetString() ?? _options.DefaultRegion;
        var store = item.GetProperty("store_channel").GetString() ?? _options.DefaultStore;
        var version = item.GetProperty("game_version").GetString() ?? _options.DefaultGameVersion;
        var playerName = await GetPlayerNameAsync(verifiedId, cancellationToken);
        return new PlayerContextResult(true, verifiedId, region, store, version, PlayerName: playerName);
    }

    private async Task<string?> GetPlayerNameAsync(string userId, CancellationToken cancellationToken)
    {
        var characterName = await GetCurrentCharacterNameAsync(userId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(characterName))
        {
            return characterName;
        }

        return await GetProfileNameAsync(userId, cancellationToken);
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
            _logger.LogWarning(ex, "Could not read active character name for a WebShop player");
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
            _logger.LogWarning(ex, "Could not read display name for a WebShop player");
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
