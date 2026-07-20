using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Storage.Nodes;

/// <summary>
/// Canonical storage put node for exact <see cref="FlowContent"/> values and
/// normal typed operation results.
/// </summary>
public sealed class FlowContentStoragePutNode : IFlowNode
{
    private readonly IStorageStore _store;
    private readonly StoragePutOptions _options;
    private readonly TimeProvider _clock;
    private readonly StorageOperationPipeline<StorageContentPutRequest, StoragePutOutcome> _pipeline;

    public FlowContentStoragePutNode(
        IStorageStore store,
        StoragePutOptions? options = null,
        TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? StoragePutOptions.Default;
        _clock = clock ?? TimeProvider.System;
        _pipeline = new(
            _options.BoundedCapacity,
            ProcessAsync);
    }

    public ITargetBlock<FlowMessage<StorageContentPutRequest>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<StoragePutOutcome>>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task<FlowMessage<FlowResult<StoragePutOutcome>>> ProcessAsync(
        FlowMessage<StorageContentPutRequest> message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;
        string? collection = input?.Collection ?? _options.Collection;
        string? key = input?.Key;

        try
        {
            if (input is null)
            {
                throw new StorageContentOperationException(
                    StorageErrorCodeNames.InvalidRequest,
                    "storage.put requires an input request.");
            }

            var request = StorageContentNodeSupport.NormalizeRequest(
                "put",
                () =>
                {
                    collection = StorageNodeSupport.ResolveCollection(
                        "storage.put",
                        input.Collection,
                        _options.Collection);
                    key = StorageNodeSupport.ResolveKey("storage.put", input.Key);
                    var mode = StorageNodeSupport.ResolveWriteMode(
                        "storage.put",
                        input.Mode,
                        _options.Mode);
                    return StorageContentNodeSupport.CreatePutRequest(
                        input,
                        collection,
                        mode,
                        message.CorrelationId);
                });
            var resolvedCollection = collection!;
            var resolvedKey = key!;

            var stored = await _store.PutAsync(request, cancellationToken).ConfigureAwait(false)
                ?? throw new StorageContentOperationException(
                    StorageErrorCodeNames.PutFailed,
                    "storage.put store returned a null record.");
            StorageContentNodeSupport.ValidateIdentity(
                stored,
                resolvedCollection,
                resolvedKey,
                "put");
            var contentRecord = StorageContentEnvelopeCodec.Decode(stored);
            var outcome = new StoragePutOutcome
            {
                Collection = resolvedCollection,
                Key = resolvedKey,
                Version = contentRecord.Version,
                StoredAt = contentRecord.StoredAt,
                ExpiresAt = contentRecord.ExpiresAt,
                Record = _options.EmitStoredRecord ? contentRecord : null
            };
            var timestamp = _clock.GetUtcNow();
            _pipeline.PublishEvent(StorageContentNodeSupport.CreateEvent(
                message,
                timestamp,
                StorageDiagnosticNames.PutStored,
                FlowEventLevel.Information,
                "storage.put stored content.",
                StorageResultKinds.PutStored,
                isError: false,
                "put",
                resolvedCollection,
                resolvedKey,
                version: contentRecord.Version));
            return message.With(FlowResult<StoragePutOutcome>.Success(
                StorageResultKinds.PutStored,
                outcome,
                timestamp));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = StorageContentNodeSupport.Classify(
                exception,
                "put",
                StorageErrorCodeNames.PutFailed);
            return Failure(
                message,
                failure.Code,
                failure.Message,
                collection,
                key,
                exception);
        }
    }

    private FlowMessage<FlowResult<StoragePutOutcome>> Failure(
        FlowMessage<StorageContentPutRequest> message,
        string code,
        string text,
        string? collection,
        string? key,
        Exception exception)
    {
        var timestamp = _clock.GetUtcNow();
        var error = StorageContentNodeSupport.CreateError(
            code,
            text,
            "put",
            collection,
            key,
            exception);
        _pipeline.PublishEvent(StorageContentNodeSupport.CreateEvent(
            message,
            timestamp,
            StorageDiagnosticNames.PutFailed,
            FlowEventLevel.Warning,
            text,
            StorageResultKinds.PutFailed,
            isError: true,
            "put",
            collection,
            key,
            errorCode: code));
        return message.With(FlowResult<StoragePutOutcome>.Failure(
            StorageResultKinds.PutFailed,
            error,
            timestamp));
    }
}
