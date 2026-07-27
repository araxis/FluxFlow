using FluxFlow.Components.Designer;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Nodes;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Payloads.Composition;

public static class PayloadsServiceCollectionExtensions
{
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                InspectDescriptor
            ],
            PayloadsComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor InspectDescriptor { get; } = new(
        PayloadsComponentDefinition.Types.Inspect,
        CreatePayloadInspectNode,
        inputs:
        [
            ComponentPorts.Metadata<FlowContent>(
                PayloadsComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<PayloadInspectionResult>(
                PayloadsComponentDefinition.Ports.Output)
        ],
        options: PayloadsComponentDefinition.CreateOptions(PayloadsComponentDefinition.Types.Inspect),
        resources: PayloadsComponentDefinition.CreateResources(PayloadsComponentDefinition.Types.Inspect));

    public static IServiceCollection AddPayloadsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreatePayloadInspectNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<PayloadInspectOptions>();
        var clock = context.GetResource<TimeProvider>(
            PayloadsComponentDefinition.Resources.Clock);
        var node = new PayloadInspectNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<FlowContent>(
                    PayloadsComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<PayloadInspectionResult>(
                    PayloadsComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
