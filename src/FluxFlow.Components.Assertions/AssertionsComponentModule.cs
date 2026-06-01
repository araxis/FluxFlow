using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Assertions;

public sealed class AssertionsComponentModule : IFlowNodeModule
{
    public AssertionsComponentModule(AssertionsComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                AssertionsComponentTypes.Assert,
                context => AssertionNodeFactory.CreateAssert(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
