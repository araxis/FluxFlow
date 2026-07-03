using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Nodes;

/// <summary>
/// A standalone storage query node over an injected <see cref="IStorageStore"/>. Post a
/// <c>FlowMessage&lt;StorageQueryRequest&gt;</c> to <c>Input</c>; the node queries the
/// store and broadcasts a single <c>FlowMessage&lt;StorageQueryResult&gt;</c> on
/// <c>Output</c>. When <see cref="StorageQueryOptions.EmitRecordOutputs"/> is set it also
/// fans each matched record to the <c>Records</c> port as a
/// <c>FlowMessage&lt;StorageRecord&gt;</c>, each carrying the same correlation id. Store
/// failures surface on <c>Errors</c> (with the original correlation id) and the node
/// keeps processing. Works with nothing but <c>new StorageQueryNode(store)</c> — no engine.
/// </summary>
public sealed class StorageQueryNode : FlowNode<StorageQueryRequest, StorageQueryResult>
{
    private readonly IStorageStore _store;
    private readonly StorageQueryOptions _options;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowMessage<StorageRecord>> _records;

    public StorageQueryNode(
        IStorageStore store,
        StorageQueryOptions? options = null,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (options ?? StorageQueryOptions.Default).BoundedCapacity
        })
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? StorageQueryOptions.Default;
        _clock = clock ?? TimeProvider.System;

        _records = AddOutput<FlowMessage<StorageRecord>>();
    }

    /// <summary>Each matched record; broadcast, carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<StorageRecord>> Records => _records;

    protected override async Task ProcessAsync(FlowMessage<StorageQueryRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        StorageQueryRequest request;
        try
        {
            request = NormalizeRequest(input);
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.InvalidRequest,
                $"storage.query request is invalid: {exception.Message}",
                message,
                input,
                exception);
            return;
        }

        try
        {
            var records = await _store.QueryAsync(request, Stopping).ConfigureAwait(false);
            var copiedRecords = ValidateAndCopyRecords(
                records,
                request,
                _clock.GetUtcNow());

            // Carry the correlation id forward onto the result and each record.
            Emit(message.With(CreateResult(request, copiedRecords)));

            if (_options.EmitRecordOutputs)
            {
                foreach (var record in copiedRecords)
                {
                    _records.Post(message.With(record));
                }
            }

            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = StorageDiagnosticNames.QueryCompleted,
                Level = FlowEventLevel.Information,
                Message = "storage.query completed.",
                Attributes = StorageNodeSupport.CreateCollectionAttributes(
                    "query",
                    request.Collection!,
                    request.CorrelationId,
                    copiedRecords.Length,
                    request.Limit)
            });
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.QueryFailed,
                $"storage.query failed: {exception.Message}",
                message,
                request,
                exception);
        }
    }

    private StorageQueryResult CreateResult(
        StorageQueryRequest request,
        IReadOnlyList<StorageRecord> records)
        => new()
        {
            Timestamp = _clock.GetUtcNow(),
            Operation = "query",
            Collection = request.Collection!,
            Succeeded = true,
            Count = records.Count,
            Records = _options.EmitRecordsInResult ? records.ToArray() : [],
            CorrelationId = request.CorrelationId
        };

    private static StorageRecord[] ValidateAndCopyRecords(
        IReadOnlyList<StorageRecord>? records,
        StorageQueryRequest request,
        DateTimeOffset now)
    {
        if (records is null)
        {
            throw new InvalidOperationException(
                "storage.query store returned a null record collection.");
        }

        if (records.Count > request.Limit!.Value)
        {
            throw new InvalidOperationException(
                "storage.query store returned more records than the requested limit.");
        }

        return records
            .Select(record => ValidateAndCopyRecord(record, request, now))
            .ToArray();
    }

    private static StorageRecord ValidateAndCopyRecord(
        StorageRecord? record,
        StorageQueryRequest request,
        DateTimeOffset now)
    {
        if (record is null)
        {
            throw new InvalidOperationException(
                "storage.query store returned a null record.");
        }

        if (!StringComparer.Ordinal.Equals(record.Collection, request.Collection))
        {
            throw new InvalidOperationException(
                "storage.query store returned a record for a different collection.");
        }

        if (!StorageQueryMatcher.IsMatch(record, request, now))
        {
            throw new InvalidOperationException(
                "storage.query store returned a record that does not match the query.");
        }

        return StorageNodeSupport.CopyRecord(record);
    }

    private StorageQueryRequest NormalizeRequest(StorageQueryRequest input)
    {
        var request = input with
        {
            Collection = StorageNodeSupport.ResolveCollection(
                "storage.query",
                input.Collection,
                _options.Collection),
            KeyPrefix = StorageNodeSupport.Normalize(input.KeyPrefix),
            Attributes = StorageNodeSupport.CopyAttributes(input.Attributes),
            IncludeExpired = input.IncludeExpired ?? _options.IncludeExpired,
            Offset = input.Offset ?? _options.Offset,
            Limit = input.Limit ?? _options.Limit,
            CorrelationId = StorageNodeSupport.Normalize(input.CorrelationId)
        };
        StorageQueryMatcher.Validate(request);
        return request;
    }

    private void ReportError(
        int code,
        string message,
        FlowMessage<StorageQueryRequest> source,
        StorageQueryRequest input,
        Exception? exception)
    {
        var collection = StorageNodeSupport.Normalize(input.Collection)
            ?? StorageNodeSupport.Normalize(_options.Collection)
            ?? "(missing)";
        var correlationId = StorageNodeSupport.Normalize(input.CorrelationId);
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = StorageNodeSupport.CreateCollectionContext(
                "query",
                collection,
                correlationId),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = StorageDiagnosticNames.QueryFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = StorageNodeSupport.CreateCollectionAttributes(
                "query",
                collection,
                correlationId)
        });
    }
}
