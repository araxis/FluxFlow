using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Storage.Nodes;

/// <summary>
/// Canonical storage get node that returns found, missing, and failed outcomes
/// through one normal result output.
/// </summary>
public sealed class StorageGetNode : IFlowNode
{
    private readonly IStorageStore _store;
    private readonly StorageGetOptions _options;
    private readonly TimeProvider _clock;
    private readonly StorageOperationPipeline<StorageGetRequest, StorageGetOutcome> _pipeline;

    public StorageGetNode(
        IStorageStore store,
        StorageGetOptions? options = null,
        TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? StorageGetOptions.Default;
        _clock = clock ?? TimeProvider.System;
        _pipeline = new(_options.BoundedCapacity, ProcessAsync);
    }

    public ITargetBlock<FlowMessage<StorageGetRequest>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<StorageGetOutcome>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task<FlowMessage<StorageGetOutcome>> ProcessAsync(
        FlowMessage<StorageGetRequest> message,
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
                    "storage.get requires an input request.");
            }

            var request = StorageNodeSupport.NormalizeRequest(
                "get",
                () => StorageNodeSupport.NormalizeGet(
                    input,
                    _options.Collection,
                    _options.IncludeExpired));
            collection = request.Collection;
            key = request.Key;
            var stored = await _store.GetAsync(request, cancellationToken).ConfigureAwait(false);
            StorageContentRecord? record = null;
            if (stored is not null)
            {
                StorageNodeSupport.ValidateIdentity(stored, collection!, key, "get");
                record = StorageContentRecordMapper.Decode(stored);
            }

            var found = record is not null;
            var outcome = new StorageGetOutcome
            {
                Collection = collection!,
                Key = key,
                Found = found,
                Record = record
            };
            var timestamp = _clock.GetUtcNow();
            var kind = found
                ? StorageResultKinds.GetFound
                : StorageResultKinds.GetNotFound;
            _pipeline.PublishEvent(StorageNodeSupport.CreateEvent(
                message,
                timestamp,
                found ? StorageDiagnosticNames.GetFound : StorageDiagnosticNames.GetNotFound,
                FlowEventLevel.Information,
                found ? "storage.get found content." : "storage.get did not find content.",
                kind,
                isError: false,
                "get",
                collection,
                key,
                version: record?.Version));
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
                "get",
                StorageErrorCodeNames.GetFailed);
            var timestamp = _clock.GetUtcNow();
            var error = StorageNodeSupport.CreateError(
                failure.Code,
                failure.Message,
                "get",
                collection,
                key,
                exception);
            _pipeline.PublishEvent(StorageNodeSupport.CreateEvent(
                message,
                timestamp,
                StorageDiagnosticNames.GetFailed,
                FlowEventLevel.Warning,
                failure.Message,
                StorageResultKinds.GetFailed,
                isError: true,
                "get",
                collection,
                key,
                errorCode: failure.Code));
            return message.WithError<StorageGetOutcome>(error);
        }
    }
}
