using FluxFlow.ComponentPackageTemplate.Nodes;
using FluxFlow.ComponentPackageTemplate.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.ComponentPackageTemplate;

public sealed class TemplateComponentModule : IFlowNodeModule
{
    public TemplateComponentModule(TemplateComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                TemplateComponentTypes.Enrich,
                context => TemplateEnrichNode.Create(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
