using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Core;

public sealed class FlowInputEndpoint
{
    internal FlowInputEndpoint(FlowTopology topology, FlowComponent owner, string name, ITargetBlock<FlowMessage> block, bool isControl = false)
        => (Topology, Owner, Address, Block, IsControl) = (topology, owner, $"{owner.Address}.{name}", block, isControl);

    internal FlowTopology Topology { get; }
    internal FlowComponent Owner { get; }
    internal ITargetBlock<FlowMessage> Block { get; }
    public string Address { get; }
    public bool IsControl { get; }

    public FlowLink LinkFrom(FlowOutputEndpoint output, Predicate<FlowMessage>? when = null,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        Action<FlowMessage, Exception>? onRejected = null)
        => output.LinkTo(this, when, delivery, onRejected);
}

public sealed class FlowOutputEndpoint
{
    internal FlowOutputEndpoint(FlowTopology topology, FlowComponent owner, string name, ISourceBlock<FlowMessage> block)
        => (Topology, Owner, Address, Block) = (topology, owner, $"{owner.Address}.{name}", block);

    internal FlowTopology Topology { get; }
    internal FlowComponent Owner { get; }
    internal ISourceBlock<FlowMessage> Block { get; }
    public string Address { get; }

    public FlowLink LinkTo(FlowInputEndpoint input, Predicate<FlowMessage>? when = null,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        Action<FlowMessage, Exception>? onRejected = null)
        => Topology.Link(this, input, when, delivery, fallback: false, onRejected);

    public FlowLink OtherwiseTo(FlowInputEndpoint input, FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        Action<FlowMessage, Exception>? onRejected = null)
        => Topology.Link(this, input, condition: null, delivery, fallback: true, onRejected);

    /// <summary>Explicitly discard messages for which no ordinary or fallback route exists.</summary>
    public void DiscardUnmatched() => Topology.DiscardUnmatched(this);
}

public enum FlowDeliveryPolicy
{
    /// <summary>Wait for target capacity; rejection or condition failure faults the graph.</summary>
    Reliable,
    /// <summary>Attempt immediate admission; rejection or condition failure faults the graph.</summary>
    RejectWhenFull,
    /// <summary>
    /// Attempt immediate admission and report rejection or condition failure to the required
    /// handler without faulting the graph. Future messages still use this route.
    /// This does not isolate component lifetime failures.
    /// </summary>
    IsolateRejection
}

/// <summary>One frozen-topology link shared by input-first and output-first definition.</summary>
public sealed class FlowLink
{
    internal FlowLink(FlowOutputEndpoint output, FlowInputEndpoint input, Predicate<FlowMessage>? condition,
        FlowDeliveryPolicy delivery, bool fallback, Action<FlowMessage, Exception>? onRejected)
        => (Output, Input, Condition, Delivery, IsFallback, OnRejected) =
            (output, input, condition, delivery, fallback, onRejected);

    public FlowOutputEndpoint Output { get; }
    public FlowInputEndpoint Input { get; }
    public Predicate<FlowMessage>? Condition { get; }
    public FlowDeliveryPolicy Delivery { get; }
    public bool IsFallback { get; }

    /// <summary>
    /// Synchronous rejection handler, required only for IsolateRejection. Receives the original
    /// message and failure. Handler exceptions cannot interrupt sibling delivery; handlers
    /// must return promptly and own any asynchronous work they initiate.
    /// </summary>
    public Action<FlowMessage, Exception>? OnRejected { get; }

    internal void ReportRejection(FlowMessage message, Exception error)
    {
        try { OnRejected!(message, error); }
        catch { /* Rejection reporting cannot break sibling delivery. */ }
    }
}
