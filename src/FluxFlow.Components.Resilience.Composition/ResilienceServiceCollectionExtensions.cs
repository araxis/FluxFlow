using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Nodes;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Composition;
using FluxFlow.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Resilience.Composition;

public static class ResilienceServiceCollectionExtensions
{
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                RetryDescriptor
            ],
            ResilienceComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor RetryDescriptor { get; } = new(
        ResilienceComponentDefinition.Types.Retry,
        CreateFlowRetryNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(ResilienceComponentDefinition.Ports.Input),
            ComponentPorts.SignalMetadata(ResilienceComponentDefinition.Ports.Ack),
            ComponentPorts.SignalMetadata(ResilienceComponentDefinition.Ports.Nak),
            ComponentPorts.SignalMetadata(ResilienceComponentDefinition.Ports.Cancel)
        ],
        outputs:
        [
            ComponentPorts.Metadata<RetrySignal<JsonElement>>(
                ResilienceComponentDefinition.Ports.Output)
        ],
        options: ResilienceComponentDefinition.CreateOptions(ResilienceComponentDefinition.Types.Retry),
        resources: ResilienceComponentDefinition.CreateResources(ResilienceComponentDefinition.Types.Retry));

    public static IServiceCollection AddResilienceComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateFlowRetryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowRetryOptions>();
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            options = options with
            {
                Name = $"{context.WorkflowName}.{context.ComponentName}"
            };
        }

        var node = new FlowRetryNode(
            options,
            context.GetResource<TimeProvider>(ResilienceComponentDefinition.Resources.Clock),
            context.GetResource<IRetryJitterSource>(ResilienceComponentDefinition.Resources.Jitter));
        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ResilienceComponentDefinition.Ports.Input,
                    node.Input),
                ComponentPorts.SignalInput(
                    ResilienceComponentDefinition.Ports.Ack,
                    node.Ack),
                ComponentPorts.SignalInput(
                    ResilienceComponentDefinition.Ports.Nak,
                    node.Nak),
                ComponentPorts.SignalInput(
                    ResilienceComponentDefinition.Ports.Cancel,
                    node.Cancel)
            ],
            outputs:
            [
                ComponentPorts.Output<RetrySignal<JsonElement>>(
                    ResilienceComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
