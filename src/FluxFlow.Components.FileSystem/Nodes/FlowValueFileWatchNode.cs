using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.FileSystem.Nodes;

/// <summary>
/// Canonical file-watch source that emits immutable workflow objects and uses
/// completion faults for isolated source failures.
/// </summary>
public sealed class FlowValueFileWatchNode : IFlowSource
{
    private readonly FileWatchNode _source;
    private readonly FlowValueFileSystemSourceProjection<FileWatchEvent> _projection;

    public FlowValueFileWatchNode(
        FileWatchOptions options,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _source = new FileWatchNode(options, clock);
        _projection = new FlowValueFileSystemSourceProjection<FileWatchEvent>(
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

    private static FlowValue ToFlowValue(FileWatchEvent watchEvent)
        => FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["timestamp"] = FlowValue.From(watchEvent.Timestamp),
            ["path"] = FlowValue.From(watchEvent.Path),
            ["directory"] = FlowValue.From(watchEvent.Directory),
            ["name"] = OptionalValue(watchEvent.Name),
            ["changeType"] = FlowValue.From(watchEvent.ChangeType.ToString()),
            ["oldPath"] = OptionalValue(watchEvent.OldPath),
            ["oldName"] = OptionalValue(watchEvent.OldName)
        });

    private static FlowValue OptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? FlowValue.Null : FlowValue.From(value.Trim());
}
