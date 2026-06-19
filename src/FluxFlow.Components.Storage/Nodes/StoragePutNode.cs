using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Storage.Nodes;

/// <summary>
/// A standalone storage write node — a "blockified" put over an injected
/// <see cref="IStorageStore"/>. Post a <c>FlowMessage&lt;StoragePutRequest&gt;</c> to
/// <c>Input</c>; the node writes the record through the injected store and broadcasts
/// a <c>FlowMessage&lt;StorageResult&gt;</c> on <c>Output</c> carrying the same
/// correlation id (failures on <c>Errors</c>, a note on <c>Events</c>). The host owns
/// the store lifetime; the node never opens or disposes it. Works with nothing but
/// <c>new StoragePutNode(store)</c> — no engine.
/// </summary>
public sealed class StoragePutNode : FlowNode<StoragePutRequest, StorageResult>
{
    private readonly IStorageStore _store;
    private readonly StoragePutOptions _options;
    private readonly TimeProvider _clock;

    public StoragePutNode(
        IStorageStore store,
        StoragePutOptions? options = null,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (options ?? StoragePutOptions.Default).BoundedCapacity
        })
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? StoragePutOptions.Default;
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ProcessAsync(FlowMessage<StoragePutRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        StoragePutRequest request;
        try
        {
            request = NormalizeRequest(input);
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.InvalidRequest,
                $"storage.put request is invalid: {exception.Message}",
                message,
                input,
                exception);
            return;
        }

        try
        {
            var record = await _store.PutAsync(request, Stopping).ConfigureAwait(false);
            ValidateRecord(record, request);
            var result = StorageNodeSupport.CreateRecordResult(
                "put",
                record,
                _options.EmitStoredRecord,
                request.CorrelationId,
                _clock);

            // Carry the correlation id forward onto the result.
            Emit(message.With(result));
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = StorageDiagnosticNames.PutStored,
                Level = FlowEventLevel.Information,
                Message = "storage.put stored record.",
                Attributes = StorageNodeSupport.CreateOperationAttributes(
                    "put",
                    request.Collection!,
                    request.Key,
                    request.CorrelationId,
                    record.Version)
            });
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.PutFailed,
                $"storage.put failed: {exception.Message}",
                message,
                request,
                exception);
        }
    }

    private StoragePutRequest NormalizeRequest(StoragePutRequest input)
        => input with
        {
            Collection = StorageNodeSupport.ResolveCollection(
                "storage.put",
                input.Collection,
                _options.Collection),
            Key = StorageNodeSupport.ResolveKey("storage.put", input.Key),
            Attributes = StorageNodeSupport.CopyAttributes(input.Attributes),
            Mode = input.Mode ?? _options.Mode,
            CorrelationId = StorageNodeSupport.Normalize(input.CorrelationId),
            ContentType = StorageNodeSupport.Normalize(input.ContentType)
        };

    private static void ValidateRecord(StorageRecord record, StoragePutRequest request)
    {
        if (!StringComparer.Ordinal.Equals(record.Collection, request.Collection))
        {
            throw new InvalidOperationException(
                "storage.put store returned a record for a different collection.");
        }

        if (!StringComparer.Ordinal.Equals(record.Key, request.Key))
        {
            throw new InvalidOperationException(
                "storage.put store returned a record for a different key.");
        }
    }

    private void ReportError(
        int code,
        string message,
        FlowMessage<StoragePutRequest> source,
        StoragePutRequest input,
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
                "put",
                collection,
                key,
                correlationId),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = StorageDiagnosticNames.PutFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = StorageNodeSupport.CreateOperationAttributes(
                "put",
                collection,
                key,
                correlationId)
        });
    }
}
