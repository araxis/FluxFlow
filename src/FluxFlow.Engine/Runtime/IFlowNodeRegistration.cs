using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public interface IFlowNodeRegistration
{
    NodeType Type { get; }

    RuntimeNode Create(RuntimeNodeFactoryContext context);
}
