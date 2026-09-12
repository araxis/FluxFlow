using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal sealed class ApplicationOutputLink(
    ApplicationOutputPort owner,
    IApplicationRevisionRoute route) : IApplicationRevisionRoute, IDisposable
{
    private readonly CancellationTokenSource _unlinked = new();
    private int _disposed;

    public ApplicationAddress Source => route.Source;
    public ApplicationAddress Target => route.Target;
    public bool IsFallback => route.IsFallback;

    public bool IsMatch(FlowMessage message)
        => Volatile.Read(ref _disposed) == 0 && route.IsMatch(message);

    public async ValueTask DeliverAsync(FlowMessage message, CancellationToken cancellationToken)
    {
        // Register before checking disposal so an unlink racing admission cancels it.
        // The source stays usable for that race; it owns no timer or wait handle.
        using var delivery = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _unlinked.Token);
        if (Volatile.Read(ref _disposed) != 0)
            return;
        try { await route.DeliverAsync(message, delivery.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_unlinked.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _unlinked.Cancel();
        owner.RemoveLink(this);
    }
}

internal sealed class ApplicationInputRevisionRoute(
    ApplicationOutputPort owner,
    IApplicationInputPort target,
    CompiledApplicationLink link) : IApplicationRevisionRoute
{
    public ApplicationAddress Source => link.Source;

    public ApplicationAddress Target => link.Target;
    public bool IsFallback => link.IsFallback;

    public bool IsMatch(FlowMessage message) => owner.IsMatch(link, message);

    public ValueTask DeliverAsync(FlowMessage message, CancellationToken cancellationToken)
        => owner.DeliverAsync(target, link, message, cancellationToken);
}
