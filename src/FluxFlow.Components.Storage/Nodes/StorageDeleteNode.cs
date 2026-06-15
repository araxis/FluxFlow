using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Nodes;

public sealed class StorageDeleteNode : FlowNodeBase, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly StorageDeleteOptions _options;
    private readonly StorageComponentOptions _componentOptions;
    private readonly StorageStoreContext _storeContext;
    private readonly ActionBlock<StorageDeleteRequest> _input;
    private readonly BufferBlock<StorageResult> _result;
    private readonly CancellationTokenSource _processingCancellation = new();
    private StorageStoreLease? _lease;
    private bool _startRequested;
    private bool _disposed;

    internal StorageDeleteNode(
        StorageDeleteOptions options,
        StorageComponentOptions componentOptions,
        StorageStoreContext storeContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _componentOptions = componentOptions ?? throw new ArgumentNullException(nameof(componentOptions));
        _storeContext = storeContext ?? throw new ArgumentNullException(nameof(storeContext));

        _input = new ActionBlock<StorageDeleteRequest>(
            DeleteAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _result = new BufferBlock<StorageResult>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        _input.Completion.ContinueWith(
            CompleteOutput,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(_result.Completion);
    }

    public ITargetBlock<StorageDeleteRequest> Input => _input;

    public ISourceBlock<StorageResult> Result => _result;

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (_startRequested)
            {
                throw new InvalidOperationException("storage.delete node has already started.");
            }

            _startRequested = true;
        }

        try
        {
            _lease = await StorageNodeSupport.OpenStoreAsync(
                _componentOptions.StoreFactory,
                _storeContext,
                cancellationToken).ConfigureAwait(false);
            TryEmitDiagnostic(
                StorageDiagnosticNames.StoreOpened,
                message: "Opened storage store.",
                attributes: StorageNodeSupport.CreateStoreAttributes(_storeContext));
        }
        catch (Exception exception)
        {
            TryReportError(
                StorageErrorCodes.StoreUnavailable,
                $"Storage store failed to open: {exception.Message}",
                exception,
                StorageNodeSupport.CreateStoreContextText(_storeContext));
            TryEmitDiagnostic(
                StorageDiagnosticNames.StoreOpenFailed,
                FlowDiagnosticLevel.Error,
                "Storage store failed to open.",
                exception,
                StorageNodeSupport.CreateStoreAttributes(_storeContext));
            lock (_stateLock)
            {
                _startRequested = false;
            }

            throw;
        }
    }

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
            if (_lease is not null)
            {
                await _lease.DisposeAsync().ConfigureAwait(false);
            }

            _processingCancellation.Dispose();
        }
    }

    private async Task DeleteAsync(StorageDeleteRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var store = _lease?.Store;
        if (store is null)
        {
            ReportError(
                StorageErrorCodes.NotStarted,
                "storage.delete has not started.",
                input,
                exception: null);
            return;
        }

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
                input,
                exception);
            return;
        }

        try
        {
            var result = await store.DeleteAsync(
                request,
                _processingCancellation.Token).ConfigureAwait(false);
            ValidateResult(result, request);

            if (result.Found || _options.EmitMissingAsResult)
            {
                await _result.SendAsync(
                    CopyResult(result),
                    _processingCancellation.Token).ConfigureAwait(false);
            }

            TryEmitDiagnostic(
                result.Found
                    ? StorageDiagnosticNames.DeleteDeleted
                    : StorageDiagnosticNames.DeleteMissing,
                message: result.Found
                    ? "storage.delete deleted record."
                    : "storage.delete did not find record.",
                attributes: StorageNodeSupport.CreateOperationAttributes(
                    "delete",
                    _options.Store,
                    request.Collection!,
                    request.Key,
                    request.CorrelationId,
                    result.Version));
        }
        catch (OperationCanceledException) when (_processingCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.DeleteFailed,
                $"storage.delete failed: {exception.Message}",
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
        StorageDeleteRequest input,
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
                "delete",
                _options.Store,
                collection,
                key,
                correlationId));
        TryEmitDiagnostic(
            StorageDiagnosticNames.DeleteFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            StorageNodeSupport.CreateOperationAttributes(
                "delete",
                _options.Store,
                collection,
                key,
                correlationId));
    }

    private void CompleteOutput(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_result).Fault(exception);
            return;
        }

        _result.Complete();
    }
}
