using Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging("Gateway");
builder.Services.AddWebShopTracing(builder.Configuration, "Gateway");

var authEnabled = builder.Configuration.GetValue<bool>("Auth:Enabled");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins("http://localhost:5200", "https://localhost:5200")
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

if (authEnabled)
{
    var authority = builder.Configuration["Auth:Authority"]
        ?? throw new InvalidOperationException("Auth:Enabled=true but Auth:Authority is missing.");
    var audience = builder.Configuration["Auth:Audience"]
        ?? throw new InvalidOperationException("Auth:Enabled=true but Auth:Audience is missing.");
    var requireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttpsMetadata", false);

    var validIssuers = new List<string> { authority.TrimEnd('/') };
    var extraIssuers = builder.Configuration.GetSection("Auth:ValidIssuers").Get<string[]>() ?? Array.Empty<string>();
    foreach (var issuer in extraIssuers)
    {
        if (!string.IsNullOrWhiteSpace(issuer))
        {
            validIssuers.Add(issuer.TrimEnd('/'));
        }
    }

    builder.Services
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

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = authEnabled
        ? new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build()
        : new AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true)
            .Build();

    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseWebShopRequestLogging();
app.UseCors();

if (authEnabled)
{
    app.UseAuthentication();
}
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapReverseProxy();

app.Run();

namespace WebShop.ApiGateway
{
    public sealed class Program;
}
