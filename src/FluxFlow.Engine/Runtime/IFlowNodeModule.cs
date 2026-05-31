namespace FluxFlow.Engine.Runtime;

public interface IFlowNodeModule
{
    IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
