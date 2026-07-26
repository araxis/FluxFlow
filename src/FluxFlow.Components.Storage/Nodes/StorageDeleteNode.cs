using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Storage.Nodes;

/// <summary>
/// Canonical storage delete node that always returns a deleted or missing normal
/// outcome for an accepted command.
/// </summary>
public sealed class StorageDeleteNode : IFlowNode
{
    private readonly IStorageStore _store;
    private readonly StorageDeleteOptions _options;
    private readonly TimeProvider _clock;
    private readonly StorageOperationPipeline<StorageDeleteRequest, StorageDeleteOutcome> _pipeline;

    public StorageDeleteNode(
        IStorageStore store,
        StorageDeleteOptions? options = null,
        TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? StorageDeleteOptions.Default;
        _clock = clock ?? TimeProvider.System;
        _pipeline = new(_options.BoundedCapacity, ProcessAsync);
    }

    public ITargetBlock<FlowMessage<StorageDeleteRequest>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<StorageDeleteOutcome>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task<FlowMessage<StorageDeleteOutcome>> ProcessAsync(
        FlowMessage<StorageDeleteRequest> message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Value;
        string? collection = input?.Collection ?? _options.Collection;
        string? key = input?.Key;

        try
        {
            if (input is null)
            {
                throw new StorageContentOperationException(
                    StorageErrorCodeNames.InvalidRequest,
                    "storage.delete requires an input request.");
            }

            var request = StorageNodeSupport.NormalizeRequest(
                "delete",
                () => StorageNodeSupport.NormalizeDelete(input, _options.Collection));
            collection = request.Collection;
            key = request.Key;
            var stored = await _store.DeleteAsync(request, cancellationToken).ConfigureAwait(false)
                ?? throw new StorageContentOperationException(
                    StorageErrorCodeNames.DeleteFailed,
                    "storage.delete store returned a null result.");
            if (!StringComparer.Ordinal.Equals(stored.Collection, collection) ||
                !StringComparer.Ordinal.Equals(stored.Key, key))
            {
                throw new StorageContentOperationException(
                    StorageErrorCodeNames.DeleteFailed,
                    "storage.delete store returned a result for a different identity.");
            }

            if (!stored.Succeeded)
            {
                throw new StorageContentOperationException(
                    StorageErrorCodeNames.DeleteFailed,
                    stored.Message ?? "storage.delete store reported failure.");
            }

            var outcome = new StorageDeleteOutcome
            {
                Collection = collection!,
                Key = key,
                Found = stored.Found,
                Deleted = stored.Deleted,
                Version = stored.Version
            };
            var timestamp = _clock.GetUtcNow();
            var kind = stored.Found
                ? StorageResultKinds.DeleteDeleted
                : StorageResultKinds.DeleteNotFound;
            _pipeline.PublishEvent(StorageNodeSupport.CreateEvent(
                message,
                timestamp,
                stored.Found
                    ? StorageDiagnosticNames.DeleteDeleted
                    : StorageDiagnosticNames.DeleteMissing,
                FlowEventLevel.Information,
                stored.Found
                    ? "storage.delete deleted content."
                    : "storage.delete did not find content.",
                kind,
                isError: false,
                "delete",
                collection,
                key,
                version: stored.Version));
            return message.With(outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = StorageNodeSupport.Classify(
                exception,
                "delete",
                StorageErrorCodeNames.DeleteFailed);
            var timestamp = _clock.GetUtcNow();
            var error = StorageNodeSupport.CreateError(
                failure.Code,
                failure.Message,
                "delete",
                collection,
                key,
                exception);
            _pipeline.PublishEvent(StorageNodeSupport.CreateEvent(
                message,
                timestamp,
                StorageDiagnosticNames.DeleteFailed,
                FlowEventLevel.Warning,
                failure.Message,
                StorageResultKinds.DeleteFailed,
                isError: true,
                "delete",
                collection,
                key,
                errorCode: failure.Code));
            return message.WithError<StorageDeleteOutcome>(error);
        }
    }
}
