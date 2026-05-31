using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

public sealed class RuntimeNodeBuilder
{
    private readonly RuntimeNodeFactoryContext _context;
    private readonly IFlowNode _node;
    private readonly List<InputPort> _inputs = [];
    private readonly List<OutputPort> _outputs = [];
    private int _phase;

    internal RuntimeNodeBuilder(RuntimeNodeFactoryContext context, IFlowNode node)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _phase = context.Definition.Phase;
    }

    public RuntimeNodeBuilder Phase(int phase)
    {
        _phase = phase;
        return this;
    }

    public RuntimeNodeBuilder Input<T>(
        string name,
        ITargetBlock<T> target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(target);

        _inputs.Add(new InputPort<T>(
            _context.Address.Port(new PortName(name)),
            target));
        return this;
    }

    public RuntimeNodeBuilder Output<T>(
        string name,
        ISourceBlock<T> source,
        bool drainWhenUnlinked = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);

        _outputs.Add(new OutputPort<T>(
            _context.Address.Port(new PortName(name)),
            source,
            drainWhenUnlinked));
        return this;
    }

    public RuntimeNode Build()
        => RuntimeNode.Create(
            _context.Address,
            _node,
            _inputs,
            _outputs,
            _phase,
            _context.Definition.Type);
}
