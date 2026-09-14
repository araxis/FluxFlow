using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Storage.Nodes;

/// <summary>
/// Canonical storage query node that emits one immutable content result per
/// query and no per-record branch output.
/// </summary>
public sealed class StorageQueryNode : IFlowComponent
{
    private readonly IStorageStore _store;
    private readonly StorageQueryOptions _options;
    private readonly TimeProvider _clock;
    private readonly StorageOperationPipeline<StorageQueryRequest> _pipeline;

    public StorageQueryNode(
        IStorageStore store,
        StorageQueryOptions? options = null,
        TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? StorageQueryOptions.Default;
        _clock = clock ?? TimeProvider.System;
        _pipeline = new(
            _options.BoundedCapacity,
            _clock,
            "query",
            ProcessAsync);
    }

    public FlowPorts Ports => _pipeline.Ports;

    public ITargetBlock<FlowMessage> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage> Output => _pipeline.Output;

    public ISourceBlock<FlowMessage> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task<FlowMessage> ProcessAsync(
        FlowMessage message,
        StorageQueryRequest input,
        CancellationToken cancellationToken)
    {
        string? collection = input.Collection ?? _options.Collection;

        try
        {
            var request = StorageNodeSupport.NormalizeRequest(
                "query",
                () => StorageNodeSupport.NormalizeQuery(
                    input,
                    _options.Collection,
                    _options.IncludeExpired,
                    _options.Offset,
                    _options.Limit));
            collection = request.Collection;
            var stored = await _store.QueryAsync(request, cancellationToken).ConfigureAwait(false)
                ?? throw new StorageContentOperationException(
                    StorageErrorCodeNames.QueryFailed,
                    "storage.query store returned a null record collection.");
            if (stored.Count > request.Limit!.Value)
            {
                throw new StorageContentOperationException(
                    StorageErrorCodeNames.QueryFailed,
                    "storage.query store returned more records than the requested limit.");
            }

            var now = _clock.GetUtcNow();
            var records = stored.Select(record => DecodeRecord(record, request, now)).ToArray();
            var outcome = new StorageQueryOutcome
            {
                Collection = collection!,
                Count = records.Length,
                Records = _options.EmitRecordsInResult
                    ? records
                    : Array.Empty<StorageContentRecord>()
            };
            var timestamp = _clock.GetUtcNow();
            _pipeline.PublishEvent(StorageNodeSupport.CreateEvent(
                message,
                timestamp,
                StorageDiagnosticNames.QueryCompleted,
                FlowEventLevel.Information,
                "storage.query completed.",
                StorageResultKinds.QueryCompleted,
                isError: false,
                "query",
                collection,
                key: null,
                count: records.Length));
            return message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(outcome, ValueJsonOptions)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = StorageNodeSupport.Classify(
                exception,
                "query",
                StorageErrorCodeNames.QueryFailed);
            var timestamp = _clock.GetUtcNow();
            var error = StorageNodeSupport.CreateProcessingError(
                message, this,
                failure.Code,
                failure.Message,
                "query",
                collection,
                key: null,
                exception);
            _pipeline.PublishEvent(StorageNodeSupport.CreateEvent(
                message,
                timestamp,
                StorageDiagnosticNames.QueryFailed,
                FlowEventLevel.Warning,
                failure.Message,
                StorageResultKinds.QueryFailed,
                isError: true,
                "query",
                collection,
                key: null,
                errorCode: failure.Code));
            return message.WithError(error);
        }
    }

    private static StorageContentRecord DecodeRecord(
        StorageRecord? record,
        StorageQueryRequest request,
        DateTimeOffset now)
    {
        if (record is null)
        {
            throw new StorageContentOperationException(
                StorageErrorCodeNames.StoredContentInvalid,
                "storage.query store returned a null record.");
        }

        if (!StringComparer.Ordinal.Equals(record.Collection, request.Collection) ||
            !StorageQueryMatcher.IsMatch(record, request, now))
        {
            throw new StorageContentOperationException(
                StorageErrorCodeNames.StoredContentInvalid,
                "storage.query store returned a record that does not match the query.");
        }

        return StorageContentRecordMapper.Decode(record);
    }
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
