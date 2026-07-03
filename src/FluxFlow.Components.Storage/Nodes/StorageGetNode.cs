using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Nodes;

/// <summary>
/// A standalone storage read node over an injected <see cref="IStorageStore"/>. Post a
/// <c>FlowMessage&lt;StorageGetRequest&gt;</c> to <c>Input</c>; the node reads the record
/// and broadcasts a <c>FlowMessage&lt;StorageResult&gt;</c> on <c>Output</c>. It also fans
/// the result to one of two extra ports — <c>Found</c> when the record exists,
/// <c>NotFound</c> when it does not — each carrying the same correlation id. A missing
/// record is a normal result, not a processing error; store failures surface on
/// <c>Errors</c> (with the original correlation id) and the node keeps processing.
/// Works with nothing but <c>new StorageGetNode(store)</c> — no engine.
/// </summary>
public sealed class StorageGetNode : FlowNode<StorageGetRequest, StorageResult>
{
    private readonly IStorageStore _store;
    private readonly StorageGetOptions _options;
    private readonly TimeProvider _clock;
    private readonly BroadcastBlock<FlowMessage<StorageResult>> _found;
    private readonly BroadcastBlock<FlowMessage<StorageResult>> _notFound;

    public StorageGetNode(
        IStorageStore store,
        StorageGetOptions? options = null,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (options ?? StorageGetOptions.Default).BoundedCapacity
        })
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? StorageGetOptions.Default;
        _clock = clock ?? TimeProvider.System;

        _found = AddOutput<FlowMessage<StorageResult>>();
        _notFound = AddOutput<FlowMessage<StorageResult>>();
    }

    /// <summary>Result when the record exists; broadcast, carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<StorageResult>> Found => _found;

    /// <summary>Result when the record is missing; broadcast, carries the correlation id.</summary>
    public ISourceBlock<FlowMessage<StorageResult>> NotFound => _notFound;

    protected override async Task ProcessAsync(FlowMessage<StorageGetRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        StorageGetRequest request;
        try
        {
            request = NormalizeRequest(input);
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.InvalidRequest,
                $"storage.get request is invalid: {exception.Message}",
                message,
                input,
                exception);
            return;
        }

        try
        {
            var record = await _store.GetAsync(request, Stopping).ConfigureAwait(false);
            if (record is not null)
            {
                ValidateRecord(record, request);
            }

            var result = record is null
                ? CreateMissingResult(request)
                : StorageNodeSupport.CreateRecordResult(
                    "get",
                    record,
                    includeRecord: true,
                    request.CorrelationId,
                    _clock);

            // Carry the correlation id forward onto the result and the branch.
            var outgoing = message.With(result);
            Emit(outgoing);
            (record is null ? _notFound : _found).Post(outgoing);

            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = record is null
                    ? StorageDiagnosticNames.GetNotFound
                    : StorageDiagnosticNames.GetFound,
                Level = FlowEventLevel.Information,
                Message = record is null
                    ? "storage.get did not find record."
                    : "storage.get found record.",
                Attributes = StorageNodeSupport.CreateOperationAttributes(
                    "get",
                    request.Collection!,
                    request.Key,
                    request.CorrelationId,
                    record?.Version)
            });
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.GetFailed,
                $"storage.get failed: {exception.Message}",
                message,
                request,
                exception);
        }
    }

    private StorageGetRequest NormalizeRequest(StorageGetRequest input)
        => input with
        {
            Collection = StorageNodeSupport.ResolveCollection(
                "storage.get",
                input.Collection,
                _options.Collection),
            Key = StorageNodeSupport.ResolveKey("storage.get", input.Key),
            IncludeExpired = input.IncludeExpired ?? _options.IncludeExpired,
            CorrelationId = StorageNodeSupport.Normalize(input.CorrelationId)
        };

    private StorageResult CreateMissingResult(StorageGetRequest request)
        => new()
        {
            Timestamp = _clock.GetUtcNow(),
            Operation = "get",
            Collection = request.Collection!,
            Key = request.Key,
            Succeeded = true,
            Found = false,
            Version = null,
            CorrelationId = request.CorrelationId,
            Message = "Record was not found."
        };

    private static void ValidateRecord(StorageRecord record, StorageGetRequest request)
    {
        if (!StringComparer.Ordinal.Equals(record.Collection, request.Collection))
        {
            throw new InvalidOperationException(
                "storage.get store returned a record for a different collection.");
        }

        if (!StringComparer.Ordinal.Equals(record.Key, request.Key))
        {
            throw new InvalidOperationException(
                "storage.get store returned a record for a different key.");
        }
    }

    private void ReportError(
        int code,
        string message,
        FlowMessage<StorageGetRequest> source,
        StorageGetRequest input,
        Exception? exception)
    {
        var collection = StorageNodeSupport.Normalize(input.Collection)
            ?? StorageNodeSupport.Normalize(_options.Collection)
            ?? "(missing)";
        var key = StorageNodeSupport.Normalize(input.Key) ?? "(missing)";
        var correlationId = StorageNodeSupport.Normalize(input.CorrelationId);
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = StorageNodeSupport.CreateOperationContext(
                "get",
                collection,
                key,
                correlationId),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = StorageDiagnosticNames.GetFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = StorageNodeSupport.CreateOperationAttributes(
                "get",
                collection,
                key,
                correlationId)
        });
    }
}
