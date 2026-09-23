using FluxFlow.Components.Batch;
using FluxFlow.Core.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Batching;

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
        var config = section.ReadConfiguration<BatchConfiguration>()
            ?? throw new ArgumentException($"Missing Batch configuration at '{section.Path}'.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.BatchSize);
        if (config.MaxWait <= TimeSpan.Zero || config.MaxWait.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(config.MaxWait));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.OutputCapacity);

        services.AddComponentFactory(address, provider => new Batch(new BatchOptions
        {
            BatchSize = config.BatchSize,
            MaxWait = config.MaxWait,
            OutputCapacity = config.OutputCapacity,
            TimeProvider = provider.GetService<TimeProvider>() ?? TimeProvider.System
        }))
            .AddInputPort<Batch>(address, "Input", node => node.Input)
            .AddOutputPort<Batch>(address, "Output", node => node.Output)
            .AddEventPort<Batch>(address, "Events", node => node.Events);
    }
}
