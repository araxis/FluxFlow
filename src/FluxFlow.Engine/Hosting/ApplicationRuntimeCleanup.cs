using FluxFlow.Composition;
using FluxFlow.Composition.Hosting.Snapshots;

namespace FluxFlow.Engine.Hosting;

internal static class ApplicationRuntimeCleanup
{
    internal static ValueTask DisposeComponentsAsync(IEnumerable<ComposedNode> descriptors)
        => DisposeReverseAsync(
            descriptors,
            static descriptor => descriptor.DisposeAsync(),
            "Component cleanup failed during runtime preparation.");

    internal static ValueTask DisposeSnapshotsAsync(
        IEnumerable<CompositionServiceProviderSnapshot> snapshots)
        => DisposeReverseAsync(
            snapshots,
            static snapshot => snapshot.DisposeAsync(),
            "Provider snapshot cleanup failed during runtime preparation.");

    private static async ValueTask DisposeReverseAsync<T>(
        IEnumerable<T> values,
        Func<T, ValueTask> dispose,
        string failureMessage)
    {
        List<Exception>? failures = null;
        foreach (var value in values.Reverse())
        {
            try
            {
                await dispose(value).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException(failureMessage, failures);
    }
}
