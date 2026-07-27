using FluxFlow.Components.Designer;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Http.Composition;

public static class HttpServiceCollectionExtensions
{
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                ClientDescriptor
            ],
            HttpComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor ClientDescriptor { get; } = new(
        HttpComponentDefinition.Types.Client,
        CreateClientNode,
        inputs:
        [
            ComponentPorts.Metadata<HttpClientRequest>(
                HttpComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<HttpResponseResult>(
                HttpComponentDefinition.Ports.Output)
        ],
        CompositionProcessingCapabilities.ParallelRelaxedOrder,
        options: HttpComponentDefinition.CreateOptions(HttpComponentDefinition.Types.Client),
        resources: HttpComponentDefinition.CreateResources(HttpComponentDefinition.Types.Client));

    public static IServiceCollection AddHttpComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateClientNode(
        ComponentActivationContext context)
    {
        var client = context.GetRequiredResource<HttpClient>(
            HttpComponentDefinition.Resources.Client);
        var options = context.BindConfiguration<HttpClientNodeOptions>();
        var clock = context.GetResource<TimeProvider>(
            HttpComponentDefinition.Resources.Clock);
        var node = new HttpClientNode(client, options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<HttpClientRequest>(
                    HttpComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<HttpResponseResult>(
                    HttpComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
