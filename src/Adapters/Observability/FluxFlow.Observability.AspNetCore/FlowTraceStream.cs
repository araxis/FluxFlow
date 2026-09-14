using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluxFlow.Nodes;

namespace FluxFlow.Observability;

/// <summary>Live completed FluxFlow spans. No replay and no retained workflow payloads.</summary>
public sealed class FlowTraceStream : IDisposable
{
    private readonly ConcurrentDictionary<long, Channel<FlowSpanSnapshot>> _clients = new();
    private readonly ActivityListener _listener;
    private long _nextClient;
    private int _disposed;
    public int ConnectedClients => _clients.Count;

    public FlowTraceStream()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                _clients.IsEmpty ? ActivitySamplingResult.None : ActivitySamplingResult.AllData,
            ActivityStopped = OnStopped
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>A slow client is disconnected once its bounded queue fills. Reconnect starts live.</summary>
    public async IAsyncEnumerable<FlowSpanSnapshot> ReadAsync(int capacity = 256,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var id = Interlocked.Increment(ref _nextClient);
        var channel = Channel.CreateBounded<FlowSpanSnapshot>(new BoundedChannelOptions(capacity)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, AllowSynchronousContinuations = false });
        _clients[id] = channel;
        if (Volatile.Read(ref _disposed) != 0)
        {
            _clients.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
        try
        {
            await foreach (var span in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return span;
        }
        finally
        {
            _clients.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    private void OnStopped(Activity activity)
    {
        try
        {
            if (_clients.IsEmpty) return;
            var snapshot = new FlowSpanSnapshot(activity.TraceId.ToString(), activity.SpanId.ToString(),
                activity.ParentSpanId == default ? null : activity.ParentSpanId.ToString(),
                activity.DisplayName, activity.Kind.ToString(), activity.StartTimeUtc,
                activity.Duration.TotalMilliseconds, activity.Status.ToString(),
                activity.StatusDescription, activity.TraceStateString, activity.ActivityTraceFlags.ToString(),
                Tags(activity.TagObjects),
                activity.Links.Select(link => new FlowSpanLink(link.Context.TraceId.ToString(),
                    link.Context.SpanId.ToString(), Tags(link.Tags ?? []))).ToArray(),
                activity.Events.Select(e => new FlowSpanEvent(e.Name, e.Timestamp, Tags(e.Tags))).ToArray());
            foreach (var client in _clients)
            {
                if (client.Value.Writer.TryWrite(snapshot)) continue;
                if (_clients.TryRemove(client.Key, out var slow)) slow.Writer.TryComplete();
            }
        }
        catch { /* Snapshot observers cannot interrupt the workflow. */ }
    }

    private static Dictionary<string, string?> Tags(IEnumerable<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, string?>();
        foreach (var tag in tags)
            result[tag.Key] = Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture);
        return result;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _listener.Dispose();
        foreach (var client in _clients)
            if (_clients.TryRemove(client.Key, out var channel)) channel.Writer.TryComplete();
    }
}

public sealed record FlowSpanSnapshot(string TraceId, string SpanId, string? ParentSpanId,
    string Name, string Kind, DateTime StartedAtUtc, double DurationMilliseconds, string Status,
    string? StatusDescription, string? TraceState, string TraceFlags,
    IReadOnlyDictionary<string, string?> Tags, IReadOnlyList<FlowSpanLink> Links, IReadOnlyList<FlowSpanEvent> Events);
public sealed record FlowSpanLink(string TraceId, string SpanId, IReadOnlyDictionary<string, string?> Tags);
public sealed record FlowSpanEvent(string Name, DateTimeOffset Timestamp, IReadOnlyDictionary<string, string?> Tags);
