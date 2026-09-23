using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Core.Execution;

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

/// <summary>A prepared route between native blocks, shared by input-first and output-first definition.</summary>
public sealed class FlowLink
{
    internal FlowLink(string outputAddress, ISourceBlock<FlowMessage> output, string inputAddress, ITargetBlock<FlowMessage> input, Predicate<FlowMessage>? condition,
        FlowDeliveryPolicy delivery, bool fallback, Action<FlowMessage, Exception>? onRejected)
    {
        if (!Enum.IsDefined(delivery)) throw new ArgumentOutOfRangeException(nameof(delivery));
        if (delivery == FlowDeliveryPolicy.IsolateRejection) ArgumentNullException.ThrowIfNull(onRejected);
        else if (onRejected is not null)
            throw new ArgumentException("A rejection handler requires IsolateRejection delivery.", nameof(onRejected));
        (OutputAddress, Output, InputAddress, Input, Condition, Delivery, IsFallback, OnRejected) =
            (outputAddress, output, inputAddress, input, condition, delivery, fallback, onRejected);
    }

    public string OutputAddress { get; }
    public ISourceBlock<FlowMessage> Output { get; }
    public string InputAddress { get; }
    public ITargetBlock<FlowMessage> Input { get; }
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
