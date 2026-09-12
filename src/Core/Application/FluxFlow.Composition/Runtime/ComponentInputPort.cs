using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

/// <summary>A named native input. Control targets remain owned by their component.</summary>
public sealed class ComponentInputPort
{
    public ComponentInputPort(
        string name,
        ITargetBlock<FlowMessage> target,
        ComponentPortKind kind = ComponentPortKind.Message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        Name = name.Trim();
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Kind = kind;
    }

    public string Name { get; }

    public ITargetBlock<FlowMessage> Target { get; }

    public ComponentPortKind Kind { get; }

    public Type MessageType => Kind == ComponentPortKind.Signal ? typeof(object) : typeof(FlowValue);
}
