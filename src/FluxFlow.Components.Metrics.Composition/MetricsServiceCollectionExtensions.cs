using FluxFlow.Components.Designer;
using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Nodes;
using FluxFlow.Components.Metrics.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Metrics.Composition;

public static class MetricsServiceCollectionExtensions
{
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                AggregateDescriptor
            ],
            MetricsComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor AggregateDescriptor { get; } = new(
        MetricsComponentDefinition.Types.Aggregate,
        CreateMetricsAggregateNode,
        inputs:
        [
            ComponentPorts.Metadata<MetricSampleInput>(
                MetricsComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<MetricSnapshotOutput>(
                MetricsComponentDefinition.Ports.Output)
        ],
        options: MetricsComponentDefinition.CreateOptions(MetricsComponentDefinition.Types.Aggregate),
        resources: MetricsComponentDefinition.CreateResources(MetricsComponentDefinition.Types.Aggregate));

    public static IServiceCollection AddMetricsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateMetricsAggregateNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<MetricsAggregateOptions>();
        var clock = context.GetResource<TimeProvider>(
            MetricsComponentDefinition.Resources.Clock);
        var node = new MetricsAggregateNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<MetricSampleInput>(
                    MetricsComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<MetricSnapshotOutput>(
                    MetricsComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
