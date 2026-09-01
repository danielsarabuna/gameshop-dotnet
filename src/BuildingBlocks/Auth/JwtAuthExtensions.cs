using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Auth;

/// <summary>
/// Shared zero-trust JWT setup: every service validates Keycloak tokens itself instead of
/// trusting the gateway. Driven by configuration:
///   Auth:Enabled (bool)          — master switch; false keeps the service open (local dev).
///   Auth:Authority               — token issuer used for discovery/signing keys.
///   Auth:Audience                — expected aud claim.
///   Auth:RequireHttpsMetadata    — must be true in production.
///   Auth:ValidIssuers (string[]) — extra accepted issuers (e.g. public URL vs internal hostname).
/// </summary>
public static class JwtAuthExtensions
{
    private const string DisabledAuthenticationScheme = "WebShopDisabled";
    public const string CompositeAuthenticationScheme = "WebShopJwt";
    public const string GameTicketAuthenticationScheme = "GameTicketJwt";
    public const string KeycloakAuthenticationScheme = "KeycloakJwt";
    public const string GameSessionPolicy = "webshop-game-session";

    public static IServiceCollection AddWebShopJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authEnabled = configuration.GetValue<bool>("Auth:Enabled");
        var gameTicketOptions = configuration.GetSection(GameTicketJwtOptions.SectionName).Get<GameTicketJwtOptions>() ?? new();
        var hasGameTickets = authEnabled && !string.IsNullOrWhiteSpace(gameTicketOptions.PublicKeyPemBase64);
        var authority = configuration["Auth:Authority"];
        var audience = configuration["Auth:Audience"];
        var hasKeycloak = authEnabled && !string.IsNullOrWhiteSpace(authority) && !string.IsNullOrWhiteSpace(audience);
        var defaultScheme = hasGameTickets && hasKeycloak
            ? CompositeAuthenticationScheme
            : hasGameTickets
                ? GameTicketAuthenticationScheme
                : KeycloakAuthenticationScheme;

        if (authEnabled && !hasGameTickets && !hasKeycloak)
        {
            throw new InvalidOperationException(
                "Auth:Enabled=true requires either GameTicketJwt:PublicKeyPemBase64 or Auth:Authority plus Auth:Audience.");
        }

        if (authEnabled)
        {
            var authentication = services.AddAuthentication(defaultScheme);

            if (hasGameTickets && hasKeycloak)
            {
                authentication.AddPolicyScheme(CompositeAuthenticationScheme, CompositeAuthenticationScheme, options =>
                {
                    options.ForwardDefaultSelector = context => SelectJwtScheme(
                        context.Request.Headers.Authorization.ToString(),
                        gameTicketOptions.Issuer);
                });
            }

            if (hasGameTickets)
            {
                var rsa = RSA.Create();
                rsa.ImportFromPem(GameTicketPem.Decode(gameTicketOptions.PublicKeyPemBase64));
                authentication.AddJwtBearer(GameTicketAuthenticationScheme, options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = gameTicketOptions.Issuer,
                        ValidateAudience = true,
                        ValidAudience = gameTicketOptions.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new RsaSecurityKey(rsa),
                        ClockSkew = TimeSpan.FromSeconds(30)
                    };
                });
            }

            if (hasKeycloak)
            {
                var validIssuers = new List<string> { authority!.TrimEnd('/') };
                foreach (var issuer in configuration.GetSection("Auth:ValidIssuers").Get<string[]>() ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(issuer))
                    {
                        validIssuers.Add(issuer.TrimEnd('/'));
                    }
                }

                authentication.AddJwtBearer(KeycloakAuthenticationScheme, options =>
                {
                    options.Authority = authority;
                    options.Audience = audience;
                    options.RequireHttpsMetadata = configuration.GetValue("Auth:RequireHttpsMetadata", false);
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuers = validIssuers,
                        ValidateAudience = true,
                        ValidAudience = audience,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromSeconds(30)
                    };
                });
            }
        }
        else
        {
            // RequireAuthorization() uses the default policy and still invokes the
            // authentication service. Register a no-op scheme so local open mode is valid.
            services.AddAuthentication(DisabledAuthenticationScheme)
                .AddScheme<AuthenticationSchemeOptions, DisabledAuthenticationHandler>(
                    DisabledAuthenticationScheme, _ => { });
        }

        services.AddAuthorization(options =>
        {
            // Fail-closed default: when auth is enabled, every endpoint without explicit
            // [AllowAnonymous] requires an authenticated user (webhooks opt out explicitly).
            var policy = authEnabled
                ? new AuthorizationPolicyBuilder(defaultScheme)
                    .RequireAuthenticatedUser()
                    .Build()
                : new AuthorizationPolicyBuilder()
                    .RequireAssertion(_ => true)
                    .Build();
            options.DefaultPolicy = policy;
            options.FallbackPolicy = policy;
            options.AddPolicy(GameSessionPolicy, gamePolicy => gamePolicy.RequireAssertion(context =>
                !authEnabled
                || !string.Equals(
                    context.User.FindFirst("webshop_session")?.Value,
                    "recipient",
                    StringComparison.OrdinalIgnoreCase)));
        });

        return services;
    }

    private static string SelectJwtScheme(string authorizationHeader, string gameTicketIssuer)
    {
        const string bearerPrefix = "Bearer ";
        if (!authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return KeycloakAuthenticationScheme;
        }

        var token = authorizationHeader[bearerPrefix.Length..].Trim();
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return KeycloakAuthenticationScheme;
        }

        return string.Equals(handler.ReadJwtToken(token).Issuer, gameTicketIssuer, StringComparison.Ordinal)
            ? GameTicketAuthenticationScheme
            : KeycloakAuthenticationScheme;
    }

    public static IServiceCollection AddGameTicketTokenIssuer(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(GameTicketJwtOptions.SectionName).Get<GameTicketJwtOptions>() ?? new();
        services.AddSingleton(new GameTicketTokenIssuer(options));
        return services;
    }

    public static WebApplication UseWebShopAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}

public sealed class GameTicketJwtOptions
{
    public const string SectionName = "GameTicketJwt";
    public string Issuer { get; set; } = "webshop-game-ticket";
    public string Audience { get; set; } = "webshop-api";
    public string PublicKeyPemBase64 { get; set; } = "";
    public string PrivateKeyPemBase64 { get; set; } = "";
    public int LifetimeMinutes { get; set; } = 15;
}

public sealed class GameTicketTokenIssuer
{
    private readonly GameTicketJwtOptions _options;
    private readonly RsaSecurityKey _key;

    public GameTicketTokenIssuer(GameTicketJwtOptions options)
    {
        _options = options;
        var rsa = RSA.Create();
        if (string.IsNullOrWhiteSpace(options.PrivateKeyPemBase64))
        {
            rsa.KeySize = 2048;
        }
        else
        {
            rsa.ImportFromPem(GameTicketPem.Decode(options.PrivateKeyPemBase64));
        }
        _key = new RsaSecurityKey(rsa);
    }

    public (string AccessToken, DateTimeOffset ExpiresAtUtc) Issue(
        string gameUserId,
        string region,
        string store,
        string gameVersion,
        string sessionKind = "game",
        int deliveryContractVersion = 1)
    {
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(_options.LifetimeMinutes, 1, 60));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new(System.Security.Claims.ClaimTypes.NameIdentifier, gameUserId),
                new("sub", gameUserId),
                new("region", region),
                new("store", store),
                new("game_version", gameVersion),
                new("webshop_delivery_contract", Math.Max(1, deliveryContractVersion).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("webshop_session", sessionKind),
                new("scope", string.Equals(sessionKind, "recipient", StringComparison.OrdinalIgnoreCase)
                    ? "webshop.checkout"
                    : "webshop.game")
            ],
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.RsaSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}

internal sealed class DisabledAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DisabledAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());
}

internal static class GameTicketPem
{
    public static string Decode(string base64Pem) => Encoding.UTF8.GetString(Convert.FromBase64String(base64Pem));
}
