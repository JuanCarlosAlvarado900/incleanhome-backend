using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace InCleanHome.Shared.Infrastructure.Messaging;

public static class MassTransitConfiguration
{
    public static IServiceCollection AddSharedMassTransit(this IServiceCollection services, string rabbitMqConnectionString, Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddMassTransit(x =>
        {
            configureConsumers?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitMqConnectionString);

                // Use Raw JSON serialization to interface seamlessly with Python / non-MassTransit apps
                cfg.UseRawJsonSerializer();
                cfg.UseRawJsonDeserializer();

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
