using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Storage.Nodes;

/// <summary>
/// A standalone storage delete node over an injected <see cref="IStorageStore"/>. Post a
/// <c>FlowMessage&lt;StorageDeleteRequest&gt;</c> to <c>Input</c>; the node deletes the
/// record and broadcasts a <c>FlowMessage&lt;StorageResult&gt;</c> on <c>Output</c>
/// carrying the same correlation id, reporting whether the record existed. When
/// <see cref="StorageDeleteOptions.EmitMissingAsResult"/> is false a missing delete is
/// suppressed. Store failures surface on <c>Errors</c> (with the original correlation id)
/// and the node keeps processing. Works with nothing but
/// <c>new StorageDeleteNode(store)</c> — no engine.
/// </summary>
public sealed class StorageDeleteNode : FlowNode<StorageDeleteRequest, StorageResult>
{
    private readonly IStorageStore _store;
    private readonly StorageDeleteOptions _options;
    private readonly TimeProvider _clock;

    public StorageDeleteNode(
        IStorageStore store,
        StorageDeleteOptions? options = null,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (options ?? StorageDeleteOptions.Default).BoundedCapacity
        })
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? StorageDeleteOptions.Default;
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ProcessAsync(FlowMessage<StorageDeleteRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        StorageDeleteRequest request;
        try
        {
            request = NormalizeRequest(input);
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.InvalidRequest,
                $"storage.delete request is invalid: {exception.Message}",
                message,
                input,
                exception);
            return;
        }

        try
        {
            var result = await _store.DeleteAsync(request, Stopping).ConfigureAwait(false);
            ValidateResult(result, request);

            if (result.Found || _options.EmitMissingAsResult)
            {
                // Carry the correlation id forward onto the result.
                Emit(message.With(CopyResult(result)));
            }

            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = result.Found
                    ? StorageDiagnosticNames.DeleteDeleted
                    : StorageDiagnosticNames.DeleteMissing,
                Level = FlowEventLevel.Information,
                Message = result.Found
                    ? "storage.delete deleted record."
                    : "storage.delete did not find record.",
                Attributes = StorageNodeSupport.CreateOperationAttributes(
                    "delete",
                    request.Collection!,
                    request.Key,
                    request.CorrelationId,
                    result.Version)
            });
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.DeleteFailed,
                $"storage.delete failed: {exception.Message}",
                message,
                request,
                exception);
        }
    }

    private StorageDeleteRequest NormalizeRequest(StorageDeleteRequest input)
        => input with
        {
            Collection = StorageNodeSupport.ResolveCollection(
                "storage.delete",
                input.Collection,
                _options.Collection),
            Key = StorageNodeSupport.ResolveKey("storage.delete", input.Key),
            CorrelationId = StorageNodeSupport.Normalize(input.CorrelationId)
        };

    private static void ValidateResult(StorageResult result, StorageDeleteRequest request)
    {
        if (!StringComparer.Ordinal.Equals(result.Collection, request.Collection))
        {
            throw new InvalidOperationException(
                "storage.delete store returned a result for a different collection.");
        }

        if (!StringComparer.Ordinal.Equals(result.Key, request.Key))
        {
            throw new InvalidOperationException(
                "storage.delete store returned a result for a different key.");
        }
    }

    private static StorageResult CopyResult(StorageResult result)
        => result with
        {
            Record = result.Record is null
                ? null
                : StorageNodeSupport.CopyRecord(result.Record),
            Attributes = StorageNodeSupport.CopyAttributes(result.Attributes)
        };

    private void ReportError(
        int code,
        string message,
        FlowMessage<StorageDeleteRequest> source,
        StorageDeleteRequest input,
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
                "delete",
                collection,
                key,
                correlationId),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = StorageDiagnosticNames.DeleteFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = StorageNodeSupport.CreateOperationAttributes(
                "delete",
                collection,
                key,
                correlationId)
        });
    }
}
