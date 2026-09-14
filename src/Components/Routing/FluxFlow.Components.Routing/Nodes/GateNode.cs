using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>Drops values while closed; forwards the original envelope while open.</summary>
public sealed class GateNode : FlowNode
{
    private int _open;

    public GateNode(bool initiallyOpen = false, int boundedCapacity = 128)
        : base(new FlowNodeOptions { InputCapacity = boundedCapacity, OutputCapacity = boundedCapacity })
    {
        _open = initiallyOpen ? 1 : 0;
        Open = AddControlInput(nameof(Open), (_, _) => SetOpen(true), boundedCapacity);
        Close = AddControlInput(nameof(Close), (_, _) => SetOpen(false), boundedCapacity);
    }

    public ITargetBlock<FlowMessage> Open { get; }
    public ITargetBlock<FlowMessage> Close { get; }
    public bool IsOpen => Volatile.Read(ref _open) != 0;

    protected override Task ProcessAsync(FlowMessage message)
        => IsOpen ? EmitAsync(message, Stopping).AsTask() : Task.CompletedTask;

    private Task SetOpen(bool open)
    {
        Volatile.Write(ref _open, open ? 1 : 0);
        return Task.CompletedTask;
    }
}
