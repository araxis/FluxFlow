using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Expectations;

public sealed class ExpectationsComponentModule : IFlowNodeModule
{
    public ExpectationsComponentModule()
        : this(new ExpectationsComponentOptions())
    {
    }

    public ExpectationsComponentModule(ExpectationsComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                ExpectationsComponentTypes.Expect,
                context => EventExpectationNode.Create(context, options, EventExpectationNodeKind.Expect)),
            new FlowNodeRegistration(
                ExpectationsComponentTypes.Guard,
                context => EventExpectationNode.Create(context, options, EventExpectationNodeKind.Guard))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
