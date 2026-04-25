using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Logging;

public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Replaces the default logging providers with a structured Serilog
    /// pipeline emitting CompactJson to stdout, enriched with the service
    /// name and the current Activity TraceId/SpanId for cross-service
    /// correlation. Levels and additional sinks can be overridden via the
    /// "Serilog" section in appsettings.
    /// </summary>
    public static ILoggingBuilder AddWebShopLogging(this ILoggingBuilder logging, string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        logging.ClearProviders();
        logging.Services.AddSerilog((sp, lc) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            lc.MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Yarp", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName)
                .Enrich.With(new ActivityEnricher())
                .WriteTo.Console(new RenderedCompactJsonFormatter())
                .ReadFrom.Configuration(config);
        });
        return logging;
    }

    /// <summary>
    /// Registers an OpenTelemetry tracing pipeline that captures incoming
    /// ASP.NET Core requests, outgoing HttpClient calls, and outgoing gRPC
    /// calls, and exports them via OTLP/gRPC to a collector (Jaeger by
    /// default in Docker). Reads <c>Otel:{Enabled,Endpoint}</c> from
    /// configuration. When <c>Otel:Enabled</c> is false (or absent), this
    /// is a no-op so local <c>make run-local</c> and integration tests do
    /// not try to reach an absent collector.
    /// </summary>
    public static IServiceCollection AddWebShopTracing(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!configuration.GetValue<bool>("Otel:Enabled"))
        {
            return services;
        }

        var endpoint = configuration["Otel:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = "http://jaeger:4317";
        }

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        services
            .AddOpenTelemetry()
            .ConfigureResource(rb => rb
                .AddService(serviceName: serviceName, serviceVersion: version)
                .AddAttributes(new KeyValuePair<string, object>[]
                {
                    new("deployment.environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production")
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.FilterHttpRequestMessage = req => req.RequestUri is null
                            || !req.RequestUri.AbsolutePath.StartsWith("/health", StringComparison.OrdinalIgnoreCase);
                    })
                    .AddGrpcClientInstrumentation()
                    .AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = new Uri(endpoint!);
                        opts.Protocol = OtlpExportProtocol.Grpc;
                    });
            });

        return services;
    }

    /// <summary>
    /// Registers an OpenTelemetry metrics pipeline (ASP.NET Core, HttpClient
    /// and Runtime instrumentation) that is scraped by Prometheus via the
    /// <c>/metrics</c> endpoint, mapped by <see cref="MapWebShopMetrics"/>.
    /// Gated on <c>Otel:Enabled</c> for symmetry with tracing.
    /// </summary>
    public static IServiceCollection AddWebShopMetrics(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!configuration.GetValue<bool>("Otel:Enabled"))
        {
            return services;
        }

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        services
            .AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService(serviceName, serviceVersion: version))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddPrometheusExporter();
            });

        return services;
    }

    /// <summary>
    /// Maps the Prometheus scraping endpoint at <c>/metrics</c> when metrics
    /// are enabled. Idempotent — safe to call once per app even if the
    /// pipeline was not registered.
    /// </summary>
    public static WebApplication MapWebShopMetrics(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("Otel:Enabled"))
        {
            app.MapPrometheusScrapingEndpoint();
        }
        return app;
    }

    /// <summary>
    /// Maps a JSON-shaped <c>/health</c> endpoint backed by
    /// <see cref="IHealthCheck"/> registrations. The response shape stays
    /// compatible with the previous ad-hoc handler:
    /// <code>{ "status": "ok", "checks": [ { "name": "redis", "status": "Healthy" } ] }</code>.
    /// </summary>
    public static WebApplication MapWebShopHealth(this WebApplication app, string path = "/health")
    {
        app.MapHealthChecks(path, new HealthCheckOptions
        {
            ResponseWriter = WriteHealthResponse,
            AllowCachingResponses = false
        }).AllowAnonymous();
        return app;
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var status = report.Status == HealthStatus.Healthy ? "ok" : report.Status.ToString().ToLowerInvariant();
        var payload = new
        {
            status,
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                error = e.Value.Exception?.Message
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    /// <summary>
    /// Wires the Serilog request-logging middleware. Logs one structured
    /// entry per HTTP request with method/path/status/elapsed and inherits
    /// the same TraceId/SpanId enrichment.
    /// </summary>
    public static WebApplication UseWebShopRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpCtx, elapsed, ex) =>
            {
                if (ex is not null) return LogEventLevel.Error;
                if (httpCtx.Response.StatusCode >= 500) return LogEventLevel.Error;
                if (httpCtx.Response.StatusCode >= 400) return LogEventLevel.Warning;
                if (httpCtx.Request.Path.StartsWithSegments("/health")) return LogEventLevel.Verbose;
                return LogEventLevel.Information;
            };
        });
        return app;
    }
}

internal sealed class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        if (activity.TraceId != default)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        }
        if (activity.SpanId != default)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
        }
        if (activity.ParentSpanId != default)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ParentSpanId", activity.ParentSpanId.ToString()));
        }
    }
}
