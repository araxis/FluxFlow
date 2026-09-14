using System.Text.Json;
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
public sealed class StorageGetNode : IFlowComponent
{
    private readonly IStorageStore _store;
    private readonly StorageGetOptions _options;
    private readonly TimeProvider _clock;
    private readonly StorageOperationPipeline<StorageGetRequest> _pipeline;

    public StorageGetNode(
        IStorageStore store,
        StorageGetOptions? options = null,
        TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? StorageGetOptions.Default;
        _clock = clock ?? TimeProvider.System;
        _pipeline = new(
            _options.BoundedCapacity,
            _clock,
            "get",
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
        StorageGetRequest input,
        CancellationToken cancellationToken)
    {
        string? collection = input.Collection ?? _options.Collection;
        string? key = input.Key;

        try
        {
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
                "get",
                StorageErrorCodeNames.GetFailed);
            var timestamp = _clock.GetUtcNow();
            var error = StorageNodeSupport.CreateProcessingError(
                message, this,
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
            return message.WithError(error);
        }
    }
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
