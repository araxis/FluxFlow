using FluxFlow.Engine.Ports;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimePortGeneration(
    ApplicationPortRuntime ports,
    IReadOnlyList<ApplicationRuntimePortSurfaceEntry> surface)
{
    private int _references = 1;

    internal ApplicationPortRuntime Ports { get; } = ports;

    internal IReadOnlyList<ApplicationRuntimePortSurfaceEntry> Surface { get; } = surface.ToArray();

    internal ApplicationRuntimePortGeneration Acquire()
    {
        while (true)
        {
            var current = Volatile.Read(ref _references);
            if (current <= 0)
                throw new ObjectDisposedException(nameof(ApplicationRuntimePortGeneration));
            if (Interlocked.CompareExchange(ref _references, current + 1, current) == current)
                return this;
        }
    }

    internal async ValueTask ReleaseAsync()
    {
        var remaining = Interlocked.Decrement(ref _references);
        if (remaining < 0)
        {
            throw new InvalidOperationException(
                "The application port generation was released too many times.");
        }

        if (remaining == 0)
            await Ports.DisposeAsync().ConfigureAwait(false);
    }
}
