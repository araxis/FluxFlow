using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Sources;

public sealed class SourcesComponentModule : IFlowNodeModule
{
    public SourcesComponentModule(SourcesComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                SourcesComponentTypes.Generated,
                context => SourceNodeFactory.CreateGenerated(context, options)),
            new FlowNodeRegistration(
                SourcesComponentTypes.Sequence,
                SourceNodeFactory.CreateSequence)
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
