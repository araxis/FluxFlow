using System.Collections.ObjectModel;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;

namespace FluxFlow.Nodes;

/// <summary>A standalone component declares its actual ports and lifetime, once.</summary>
public interface IFlowComponent : IFlowNode
{
    FlowPorts Ports { get; }
}

/// <summary>Immutable names over component-owned blocks; this view does not own them.</summary>
public sealed class FlowPorts
{
    public const string InputName = "Input";
    public const string OutputName = "Output";
    public const string EventsName = "Events";

    /// <summary>Names the standard input, output and event blocks without taking ownership.</summary>
    public static FlowPorts CreateStandard(
        ITargetBlock<FlowMessage> input,
        ISourceBlock<FlowMessage> output,
        ISourceBlock<FlowMessage> events)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(events);
        return new FlowPorts(
            inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { [InputName] = input },
            outputs: new Dictionary<string, ISourceBlock<FlowMessage>> { [OutputName] = output },
            events: events);
    }

    public FlowPorts(
        IReadOnlyDictionary<string, ITargetBlock<FlowMessage>>? inputs = null,
        IReadOnlyDictionary<string, ISourceBlock<FlowMessage>>? outputs = null,
        ISourceBlock<FlowMessage>? events = null,
        IReadOnlyDictionary<string, ITargetBlock<FlowMessage>>? controls = null)
    {
        Inputs = Copy(inputs);
        Outputs = Copy(outputs);
        Events = events;
        Controls = Copy(controls);
        if (Inputs.Keys.Intersect(Controls.Keys, StringComparer.Ordinal).Any())
            throw new ArgumentException("Business and control inputs must have distinct names.", nameof(controls));
    }

    public IReadOnlyDictionary<string, ITargetBlock<FlowMessage>> Inputs { get; }
    public IReadOnlyDictionary<string, ISourceBlock<FlowMessage>> Outputs { get; }
    /// <summary>Terminal control actions; these must not generate further business output.</summary>
    public IReadOnlyDictionary<string, ITargetBlock<FlowMessage>> Controls { get; }
    /// <summary>Optional best-effort observation, separate from reliable business outputs.</summary>
    public ISourceBlock<FlowMessage>? Events { get; }

    internal bool IsControlInput(ITargetBlock<FlowMessage> input)
    {
        foreach (var control in Controls.Values)
            if (ReferenceEquals(control, input)) return true;
        return false;
    }

    private static IReadOnlyDictionary<string, T> Copy<T>(IReadOnlyDictionary<string, T>? ports)
        where T : class
    {
        var copy = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (name, block) in ports ?? new Dictionary<string, T>())
        {
            ApplicationAddress.RequireConstructedSegment(name, nameof(ports), "Port name");
            ArgumentNullException.ThrowIfNull(block);
            copy.Add(name, block);
        }
        return new ReadOnlyDictionary<string, T>(copy);
    }
}
