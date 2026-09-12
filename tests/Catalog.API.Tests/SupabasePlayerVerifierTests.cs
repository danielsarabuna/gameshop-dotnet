using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Catalog.API.Configuration;
using Catalog.API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Catalog.API.Tests;

public class SupabasePlayerVerifierTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK);
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ResponseFactory?.Invoke(request) ?? ResponseToReturn);
        }
    }

    [Fact]
    public async Task ClaimTicketAsync_ShouldReturnSuccess_WhenTicketIsValid()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        var options = new SupabaseOptions { Url = "https://mock.supabase.co" };

        var json = """
        [
          {
            "user_id": "11111111-2222-3333-4444-555555555555",
            "region": "russia",
            "store_channel": "ru_store",
            "game_version": "0.0.36",
            "is_valid": true
          }
        ]
        """;

        handler.ResponseFactory = request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.RequestUri?.AbsolutePath.Contains("user_profile") == true
                    ? "[{\"data\":\"{\\\"Nickname\\\":\\\"Мирослава\\\"}\"}]"
                    : json,
                System.Text.Encoding.UTF8,
                "application/json")
        };

        var verifier = new SupabasePlayerVerifier(client, options, NullLogger<SupabasePlayerVerifier>.Instance);

        // Act
        var result = await verifier.ClaimTicketAsync(System.Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("11111111-2222-3333-4444-555555555555", result.UserId);
        Assert.Equal("russia", result.Region);
        Assert.Equal("ru_store", result.Store);
        Assert.Equal("0.0.36", result.GameVersion);
        Assert.Equal("Мирослава", result.PlayerName);
    }

    [Fact]
    public async Task ClaimTicketAsync_ShouldUseCurrentCharacterName()
    {
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        var options = new SupabaseOptions { Url = "https://mock.supabase.co" };
        var ticketJson = """
        [{
          "user_id": "11111111-2222-3333-4444-555555555555",
          "region": "russia",
          "store_channel": "ru_store",
          "game_version": "0.0.1",
          "is_valid": true
        }]
        """;
        var characterStateJson = """
        [{"data":{
          "currentState":"male-state",
          "characters":[
            {"Id":{"Value":"male-state"},"meta":{"name":"Тимофей"}},
            {"Id":{"Value":"female-state"},"meta":{"name":"Мирослава"}}
          ]
        }}]
        """;

        handler.ResponseFactory = request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                request.RequestUri?.AbsolutePath.Contains("user_character_state") == true
                    ? characterStateJson
                    : ticketJson,
                System.Text.Encoding.UTF8,
                "application/json")
        };

        var verifier = new SupabasePlayerVerifier(client, options, NullLogger<SupabasePlayerVerifier>.Instance);

        var result = await verifier.ClaimTicketAsync(System.Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("Тимофей", result.PlayerName);
    }

    [Fact]
    public async Task ClaimTicketAsync_ShouldNotExposeInfrastructureException()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = _ => throw new InvalidOperationException("private upstream detail")
        };
        var verifier = new SupabasePlayerVerifier(
            new HttpClient(handler),
            new SupabaseOptions { Url = "https://mock.supabase.co" },
            NullLogger<SupabasePlayerVerifier>.Instance);

        var result = await verifier.ClaimTicketAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("Ticket validation failed on Supabase server.", result.ErrorMessage);
        Assert.DoesNotContain("private upstream detail", result.ErrorMessage);
    }

    [Fact]
    public async Task VerifyDirectPlayerIdAsync_ShouldReturnValidResult_ForValidUuid()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        var options = new SupabaseOptions();

        var json = """
        [
          {
            "user_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            "updated_at": "2026-08-10T12:00:00Z"
          }
        ]
        """;

        handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        var verifier = new SupabasePlayerVerifier(client, options, NullLogger<SupabasePlayerVerifier>.Instance);

        // Act
        var result = await verifier.VerifyDirectPlayerIdAsync("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", result.UserId);
    }

    [Fact]
    public async Task VerifyDirectPlayerIdAsync_ShouldReturnError_ForEmptyId()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        var options = new SupabaseOptions();

        var verifier = new SupabasePlayerVerifier(client, options, NullLogger<SupabasePlayerVerifier>.Instance);

        // Act
        var result = await verifier.VerifyDirectPlayerIdAsync("", CancellationToken.None);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("Player ID cannot be empty.", result.ErrorMessage);
    }
}
