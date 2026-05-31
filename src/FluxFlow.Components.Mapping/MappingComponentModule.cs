using FluxFlow.Components.Mapping.Nodes;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mapping;

public sealed class MappingComponentModule : IFlowNodeModule
{
    public MappingComponentModule(MappingComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                MappingComponentTypes.Mapper,
                context => FlowMapperNodeFactory.Create(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
