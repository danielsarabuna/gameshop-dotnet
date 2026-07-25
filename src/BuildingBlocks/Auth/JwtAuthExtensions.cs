using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    public static IServiceCollection AddWebShopJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authEnabled = configuration.GetValue<bool>("Auth:Enabled");

        var gameTicketOptions = configuration.GetSection(GameTicketJwtOptions.SectionName).Get<GameTicketJwtOptions>() ?? new();
        if (authEnabled && !string.IsNullOrWhiteSpace(gameTicketOptions.PublicKeyPemBase64))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(GameTicketPem.Decode(gameTicketOptions.PublicKeyPemBase64));
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
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
        else if (authEnabled)
        {
            var authority = configuration["Auth:Authority"]
                ?? throw new InvalidOperationException("Auth:Enabled=true but Auth:Authority is missing.");
            var audience = configuration["Auth:Audience"]
                ?? throw new InvalidOperationException("Auth:Enabled=true but Auth:Audience is missing.");
            var requireHttpsMetadata = configuration.GetValue("Auth:RequireHttpsMetadata", false);

            var validIssuers = new List<string> { authority.TrimEnd('/') };
            foreach (var issuer in configuration.GetSection("Auth:ValidIssuers").Get<string[]>() ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(issuer))
                {
                    validIssuers.Add(issuer.TrimEnd('/'));
                }
            }

            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = authority;
                    options.Audience = audience;
                    options.RequireHttpsMetadata = requireHttpsMetadata;
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
                ? new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .Build()
                : new AuthorizationPolicyBuilder()
                    .RequireAssertion(_ => true)
                    .Build();
            options.DefaultPolicy = policy;
            options.FallbackPolicy = policy;
        });

        return services;
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
        string gameVersion)
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
                new("game_version", gameVersion)
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
