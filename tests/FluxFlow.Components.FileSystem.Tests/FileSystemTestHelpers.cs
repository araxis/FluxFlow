using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.FileSystem.Tests;

// Shared helpers for the standalone node tests: link a node's broadcast port to a
// BufferBlock sink, and manage a real temp directory for filesystem-touching tests.
internal static class FileSystemTestHelpers
{
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    public static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    public static async Task<List<FlowMessage<T>>> DrainAsync<T>(BufferBlock<FlowMessage<T>> sink)
    {
        using var cancellation = new CancellationTokenSource(TestTimeout);
        var items = new List<FlowMessage<T>>();
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

internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path) => Path = path;

    public string Path { get; }

    public static TempDirectory Create(string label)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"fluxflow-filesystem-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a watcher may still hold a handle briefly.
            }
        }
    }
}
