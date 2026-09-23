using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Source;

/// <summary>Each ordinary input emits the next sample value. Exhausted lists produce no output.</summary>
public sealed class Source : FlowTransformNode
{
    private readonly FlowValue[] _values;
    private int _next;

    public Source(SourceOptions options) : this(options, Snapshot(options)) { }

    private Source(SourceOptions options, FlowValue[] values) : base(outputCapacity: options.OutputCapacity)
    {
        _values = values;
    }

    protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        => Task.FromResult<IEnumerable<FlowMessage>>(_next < _values.Length
            ? [message.WithValue(_values[_next++])]
            : []);

    private static FlowValue[] Snapshot(SourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Values);
        var values = options.Values.ToArray();
        if (values.Any(value => value is null)) throw new ArgumentException("Use JsonFlowValue.Null for a JSON null value.", nameof(options));
        return values;
    }
}
