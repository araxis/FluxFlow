using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

/// <summary>A named native output carrying canonical workflow messages.</summary>
public sealed class ComponentOutputPort
{
    public ComponentOutputPort(string name, ISourceBlock<FlowMessage> source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public string Name { get; }

    public ISourceBlock<FlowMessage> Source { get; }

    /// <summary>True for an addressable event bridge that closes after component control work.</summary>
    public bool IsEvent { get; internal init; }

    public Type MessageType => typeof(FlowValue);
}
