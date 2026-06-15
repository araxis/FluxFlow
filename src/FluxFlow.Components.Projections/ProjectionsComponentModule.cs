using FluxFlow.Components.Projections.Nodes;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Projections;

public sealed class ProjectionsComponentModule : IFlowNodeModule
{
    public ProjectionsComponentModule()
        : this(new ProjectionsComponentOptions())
    {
    }

    public ProjectionsComponentModule(ProjectionsComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                ProjectionsComponentTypes.EventProjection,
                context => EventProjectionNodeFactory.Create(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
