using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Nodes;

public sealed class StorageGetNode : FlowNodeBase, IAsyncDisposable
{
    private const string NotAvailableMessage =
        "storage.get is not available; open the storage.store store (host ConnectAsync) before reading.";

    private readonly StorageGetOptions _options;
    private readonly IStorageStoreHandle _store;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<StorageGetRequest> _input;
    private readonly BufferBlock<StorageResult> _result;
    private readonly BufferBlock<StorageResult> _found;
    private readonly BufferBlock<StorageResult> _notFound;
    private readonly CancellationTokenSource _processingCancellation = new();
    private bool _disposed;

    internal StorageGetNode(
        StorageGetOptions options,
        IStorageStoreHandle store,
        TimeProvider clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        _input = new ActionBlock<StorageGetRequest>(
            GetAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _result = new BufferBlock<StorageResult>(blockOptions);
        _found = new BufferBlock<StorageResult>(blockOptions);
        _notFound = new BufferBlock<StorageResult>(blockOptions);
        _input.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_result.Completion, _found.Completion, _notFound.Completion));
    }

    public ITargetBlock<StorageGetRequest> Input => _input;

    public ISourceBlock<StorageResult> Result => _result;

    public ISourceBlock<StorageResult> Found => _found;

    public ISourceBlock<StorageResult> NotFound => _notFound;

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            _processingCancellation.Cancel();
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_result).Fault(exception);
            ((IDataflowBlock)_found).Fault(exception);
            ((IDataflowBlock)_notFound).Fault(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Complete();
            await Completion.ConfigureAwait(false);
        }
        finally
        {
            _processingCancellation.Dispose();
        }
    }

    private async Task GetAsync(StorageGetRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

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
                input,
                exception);
            return;
        }

        // Borrow the store the storage.store node opened. The get node never opens
        // or disposes a store; if none is open the request reports not available.
        if (!_store.TryGetStore(out var store))
        {
            ReportError(
                StorageErrorCodes.StoreNotAvailable,
                NotAvailableMessage,
                request,
                exception: null);
            return;
        }

        try
        {
            var record = await store.GetAsync(
                request,
                _processingCancellation.Token).ConfigureAwait(false);
            var result = record is null
                ? CreateMissingResult(request)
                : StorageNodeSupport.CreateRecordResult(
                    "get",
                    record,
                    includeRecord: true,
                    request.CorrelationId,
                    _clock);

            await _result.SendAsync(result, _processingCancellation.Token).ConfigureAwait(false);
            await (record is null ? _notFound : _found)
                .SendAsync(result, _processingCancellation.Token)
                .ConfigureAwait(false);
            TryEmitDiagnostic(
                record is null
                    ? StorageDiagnosticNames.GetNotFound
                    : StorageDiagnosticNames.GetFound,
                message: record is null
                    ? "storage.get did not find record."
                    : "storage.get found record.",
                attributes: StorageNodeSupport.CreateOperationAttributes(
                    "get",
                    _store.StoreName,
                    request.Collection!,
                    request.Key,
                    request.CorrelationId,
                    record?.Version));
        }
        catch (OperationCanceledException) when (_processingCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.GetFailed,
                $"storage.get failed: {exception.Message}",
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

    private void ReportError(
        int code,
        string message,
        StorageGetRequest input,
        Exception? exception)
    {
        var collection = StorageNodeSupport.Normalize(input.Collection)
            ?? StorageNodeSupport.Normalize(_options.Collection)
            ?? "(missing)";
        var key = StorageNodeSupport.Normalize(input.Key) ?? "(missing)";
        var correlationId = StorageNodeSupport.Normalize(input.CorrelationId);
        TryReportError(
            code,
            message,
            exception,
            StorageNodeSupport.CreateOperationContext(
                "get",
                _store.StoreName,
                collection,
                key,
                correlationId));
        TryEmitDiagnostic(
            StorageDiagnosticNames.GetFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            StorageNodeSupport.CreateOperationAttributes(
                "get",
                _store.StoreName,
                collection,
                key,
                correlationId));
    }

    private void CompleteOutputs(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_result).Fault(exception);
            ((IDataflowBlock)_found).Fault(exception);
            ((IDataflowBlock)_notFound).Fault(exception);
            return;
        }

        _result.Complete();
        _found.Complete();
        _notFound.Complete();
    }
}
