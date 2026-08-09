using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class ComponentEventSource
{
    public ComponentEventSource(string name, ISourceBlock<FlowEvent> source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public string Name { get; }

    public ISourceBlock<FlowEvent> Source { get; }
}
