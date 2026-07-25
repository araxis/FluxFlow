using FluxFlow.Composition.Addressing;

namespace FluxFlow.Engine.Ports;

internal sealed class ApplicationInputAttachmentLifetime(
    long generation,
    Func<long, CancellationToken, ValueTask> drain,
    Func<long, ValueTask> detach) : IApplicationInputAttachment
{
    private int _disposed;

    ValueTask IApplicationInputAttachment.DrainAsync(CancellationToken cancellationToken)
        => drain(generation, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            await detach(generation).ConfigureAwait(false);
    }
}

internal sealed class ApplicationInputRevisionLifetime(
    ApplicationAddress address,
    Type payloadType,
    string portDescription,
    Func<object?, IApplicationInputAttachment> commit,
    Action endRevision) : IApplicationInputRevision
{
    private int _committed;
    private int _disposed;

    public ApplicationAddress Address { get; } = address;

    public Type PayloadType { get; } = payloadType;

    public IAsyncDisposable Commit(object? target)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException("InputRevision");
        if (Interlocked.Exchange(ref _committed, 1) != 0)
        {
            throw new InvalidOperationException(
                $"{portDescription} '{Address}' revision was already committed.");
        }

        return commit(target);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            endRevision();
        return ValueTask.CompletedTask;
    }
}
