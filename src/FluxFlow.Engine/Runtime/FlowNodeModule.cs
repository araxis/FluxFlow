namespace FluxFlow.Engine.Runtime;

public sealed class FlowNodeModule : IFlowNodeModule
{
    public FlowNodeModule(params IFlowNodeRegistration[] registrations)
        : this((IEnumerable<IFlowNodeRegistration>)registrations)
    {
    }

    public FlowNodeModule(IEnumerable<IFlowNodeRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        Registrations = registrations
            .Select(registration => registration ?? throw new ArgumentException(
                "A flow node module cannot contain a null registration.",
                nameof(registrations)))
            .ToArray();
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
