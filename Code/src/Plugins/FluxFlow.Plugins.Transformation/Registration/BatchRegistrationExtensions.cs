using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Components.Batch;
using FluxFlow.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Batching;

public static class BatchRegistrationExtensions
{
    public static IServiceCollection AddBatch(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ComponentRegistration>("batch", Configure);
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadRequiredConfiguration<BatchConfiguration>();
        config.Validate();
        services.AddBatchNode(address, config.BatchSize, config.MaxWait, config.OutputCapacity);

        services.AddLinks(address, config);
    }
}
