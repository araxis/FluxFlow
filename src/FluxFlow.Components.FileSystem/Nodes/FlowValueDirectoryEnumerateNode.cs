using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.FileSystem.Nodes;

/// <summary>
/// Canonical directory source that emits immutable workflow objects and uses
/// completion faults for isolated source failures.
/// </summary>
public sealed class FlowValueDirectoryEnumerateNode : IFlowSource
{
    private readonly DirectoryEnumerateNode _source;
    private readonly FlowValueFileSystemSourceProjection<DirectoryEnumerateEntry> _projection;

    public FlowValueDirectoryEnumerateNode(
        DirectoryEnumerateOptions options,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _source = new DirectoryEnumerateNode(options, clock);
        _projection = new FlowValueFileSystemSourceProjection<DirectoryEnumerateEntry>(
            _source,
            _source.Output,
            _source.Errors,
            _source.Events,
            ToFlowValue,
            options.BoundedCapacity);
    }

    public ISourceBlock<FlowMessage<FlowValue>> Output => _projection.Output;

    public ISourceBlock<FlowEvent> Events => _projection.Events;

    public Task Completion => _projection.Completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _projection.StartAsync(cancellationToken);

    public void Complete() => _projection.Complete();

    public void Fault(Exception exception) => _projection.Fault(exception);

    public ValueTask DisposeAsync() => _projection.DisposeAsync();

    private static FlowValue ToFlowValue(DirectoryEnumerateEntry entry)
        => FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["enumeratedAt"] = FlowValue.From(entry.EnumeratedAt),
            ["path"] = FlowValue.From(entry.Path),
            ["directory"] = FlowValue.From(entry.Directory),
            ["name"] = FlowValue.From(entry.Name),
            ["entryType"] = FlowValue.From(entry.EntryType.ToString()),
            ["length"] = entry.Length.HasValue ? FlowValue.From(entry.Length.Value) : FlowValue.Null,
            ["createdAt"] = entry.CreatedAt.HasValue
                ? FlowValue.From(entry.CreatedAt.Value)
                : FlowValue.Null,
            ["lastModifiedAt"] = entry.LastModifiedAt.HasValue
                ? FlowValue.From(entry.LastModifiedAt.Value)
                : FlowValue.Null,
            ["attributes"] = FlowValue.From((long)entry.Attributes)
        });
}
