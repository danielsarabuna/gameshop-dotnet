using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventBus.RabbitMq;

public static class RabbitMqEventBusExtensions
{
    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        var section = configuration.GetSection("EventBus:RabbitMq");
        var options = section.Get<RabbitMqOptions>() ?? new RabbitMqOptions();

        services.AddMassTransit(bus =>
        {
            configure?.Invoke(bus);

            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(options.Host, options.Port, options.VirtualHost, h =>
                {
                    h.Username(options.Username);
                    h.Password(options.Password);
                });
                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddSingleton<IEventBus, MassTransitEventBus>();
        return services;
    }
}
