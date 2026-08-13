using BuildingBlocks.Exceptions;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging("Gateway");
builder.Services.AddWebShopTracing(builder.Configuration, "Gateway");
builder.Services.AddWebShopMetrics(builder.Configuration, "Gateway");
builder.Services.AddHealthChecks();
builder.Services.AddCustomExceptionHandler();

var authEnabled = builder.Configuration.GetValue<bool>("Auth:Enabled");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "http://localhost:5200", "https://localhost:5200")
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Configure Rate Limiting to protect downstream services against DDoS & abuse
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("global-limiter", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 10;
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

// Configure YARP with Header Transformations (Prevent spoofing and forward verified claims)
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transformContext =>
    {
        // Strip client-supplied X-User-Id header to prevent spoofing
        transformContext.RequestTransforms.Add(new RequestHeaderRemoveTransform("X-User-Id"));

        // If authenticated, forward verified User ID in X-User-Id header to internal microservices
        transformContext.AddRequestTransform(async context =>
        {
            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    context.ProxyRequest.Headers.Add("X-User-Id", userId);
                }
            }
            await ValueTask.CompletedTask;
        });
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseWebShopSecurityHeaders();
app.UseWebShopRequestLogging();
app.UseCors();
app.UseRateLimiter();

if (authEnabled)
{
    app.UseAuthentication();
}
app.UseAuthorization();

app.MapWebShopHealth();
app.MapWebShopMetrics();

app.MapReverseProxy();

app.Run();

namespace WebShop.ApiGateway
{
    public sealed class Program;
}
