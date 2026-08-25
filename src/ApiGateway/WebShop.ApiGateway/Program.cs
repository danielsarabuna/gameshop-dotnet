using BuildingBlocks.Auth;
using BuildingBlocks.Exceptions;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Logging;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging("Gateway");
builder.Services.AddWebShopTracing(builder.Configuration, "Gateway");
builder.Services.AddWebShopMetrics(builder.Configuration, "Gateway");
builder.Services.AddHealthChecks();
builder.Services.AddCustomExceptionHandler();
builder.Services.AddWebShopJwtAuthentication(builder.Configuration);

// CORS: production domains come from configuration; localhost defaults keep dev frictionless.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        policy
            .WithOrigins(configuredOrigins.Length > 0
                ? configuredOrigins
                : ["http://localhost:5173", "http://localhost:5200", "https://localhost:5200"])
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
app.UseWebShopAuth();

app.MapWebShopHealth();
app.MapWebShopMetrics();

app.MapReverseProxy();

app.Run();

namespace WebShop.ApiGateway
{
    public sealed class Program;
}
