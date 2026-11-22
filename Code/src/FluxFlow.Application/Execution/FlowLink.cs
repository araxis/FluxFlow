using FluxFlow.Application.Addressing;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Application.Execution;

/// <summary>A link declaration between native blocks, attached when the application starts.</summary>
public sealed class FlowLink
{
    internal FlowLink(PortAddress outputAddress, ISourceBlock<FlowMessage> output, PortAddress inputAddress, ITargetBlock<FlowMessage> input, Predicate<FlowMessage>? condition)
    {
        (OutputAddress, Output, InputAddress, Input, Condition) =
            (outputAddress, output, inputAddress, input, condition);
    }

    public PortAddress OutputAddress { get; }
    public ISourceBlock<FlowMessage> Output { get; }
    public PortAddress InputAddress { get; }
    public ITargetBlock<FlowMessage> Input { get; }
    public Predicate<FlowMessage>? Condition { get; }
}
