using FluxFlow.Components.Mapping;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Core.Application;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Mapping;

public static class MapperRegistrationExtensions
{
    public static IServiceCollection AddMapper(this IServiceCollection services, string? expressionBaseDirectory = null)
    {
        var directory = Path.GetFullPath(expressionBaseDirectory ?? AppContext.BaseDirectory);
        services.AddKeyedSingleton<ComponentRegistration>("mapper", (registrations, section) => Configure(registrations, section, directory));
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section, string directory)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadConfiguration<MapperConfiguration>()
            ?? throw new ArgumentException($"Missing Mapper configuration at '{section.Path}'.");
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Engine);
        ArgumentNullException.ThrowIfNull(config.Expression);
        if (config.OutputCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(config.OutputCapacity));
        var engine = config.Engine.Trim().ToLowerInvariant();
        services.AddComponentFactory(address, provider =>
        {
            try
            {
                var compiler = provider.GetRequiredKeyedService<IFlowExpressionEngine>(engine);
                var evaluate = compiler.Compile<FlowValue>(config.Expression.Read(directory));
                ValueTask<FlowValue> Map(FlowMessage message, CancellationToken cancellation)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var result = evaluate(FlowMapContext.ForMessage(message));
                    cancellation.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(result);
                }
                return new Mapper(new MapperOptions { MapAsync = Map, OutputCapacity = config.OutputCapacity });
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Mapper '{address}' using engine '{engine}' could not be built: {error.Message}", error);
            }
        }).AddInputPort<Mapper>(address, "Input", node => node.Input)
          .AddOutputPort<Mapper>(address, "Output", node => node.Output)
          .AddEventPort<Mapper>(address, node => node.Events);
    }
}
