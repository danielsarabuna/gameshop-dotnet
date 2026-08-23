using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Auth;

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddWebShopForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var value in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
            {
                if (!System.Net.IPNetwork.TryParse(value, out var network))
                {
                    throw new InvalidOperationException($"Invalid trusted proxy network '{value}'.");
                }

                options.KnownIPNetworks.Add(network);
            }
        });
        return services;
    }

    public static IApplicationBuilder UseWebShopForwardedHeaders(this IApplicationBuilder app)
        => app.UseForwardedHeaders();
}
