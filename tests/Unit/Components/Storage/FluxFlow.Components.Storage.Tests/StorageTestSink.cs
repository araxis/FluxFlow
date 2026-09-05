using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Tests;

// Standalone test helper: link a node's broadcast port to a BufferBlock sink and
// drain it, mirroring the kit's own multi-output tests and the HTTP/Timers/Validation
// reference tests. No engine ports involved.
internal static class StorageTestSink
{
    public static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    public static async Task<List<T>> DrainUntilCompletedAsync<T>(BufferBlock<T> sink)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var items = new List<T>();
        while (await sink.OutputAvailableAsync(cancellation.Token).ConfigureAwait(false))
        {
            while (sink.TryReceive(out var item))
            {
                items.Add(item);
            }
        }

        return items;
    }
}
