using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    public static IServiceCollection AddWebShopJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var authEnabled = configuration.GetValue<bool>("Auth:Enabled");

        if (authEnabled)
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

        services.AddAuthorization(options =>
        {
            // Fail-closed default: when auth is enabled, every endpoint without explicit
            // [AllowAnonymous] requires an authenticated user (webhooks opt out explicitly).
            options.FallbackPolicy = authEnabled
                ? new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .Build()
                : new AuthorizationPolicyBuilder()
                    .RequireAssertion(_ => true)
                    .Build();
        });

        return services;
    }

    public static WebApplication UseWebShopAuth(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("Auth:Enabled"))
        {
            app.UseAuthentication();
        }

        app.UseAuthorization();
        return app;
    }
}
