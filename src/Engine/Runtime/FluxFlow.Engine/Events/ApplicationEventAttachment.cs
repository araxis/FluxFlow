using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Events;

/// <summary>Owns only the native links and forwarding blocks for one revision.</summary>
internal sealed class ApplicationEventAttachment : IAsyncDisposable
{
    private readonly List<IDisposable> _links = [];
    private readonly List<ActionBlock<FlowMessage>> _targets = [];

    internal ApplicationEventAttachment(ApplicationEventStream events, string revisionId,
        IEnumerable<ComponentInstance> components)
    {
        foreach (var component in components)
        {
            var target = new ActionBlock<FlowMessage>(message => events.Publish(message, revisionId),
                new ExecutionDataflowBlockOptions { BoundedCapacity = 256 });
            _targets.Add(target);
            _links.Add(component.Activity.LinkTo(target, new DataflowLinkOptions { PropagateCompletion = true }));
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var link in _links) link.Dispose();
        foreach (var target in _targets) target.Complete();
        await Task.WhenAll(_targets.Select(target => target.Completion)).ConfigureAwait(false);
    }
}
