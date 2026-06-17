using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Nodes;

public sealed class StorageDeleteNode : FlowNodeBase, IAsyncDisposable
{
    private const string NotAvailableMessage =
        "storage.delete is not available; open the storage.store store (host ConnectAsync) before deleting.";

    private readonly StorageDeleteOptions _options;
    private readonly IStorageStoreHandle _store;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<StorageDeleteRequest> _input;
    private readonly BufferBlock<StorageResult> _result;
    private readonly CancellationTokenSource _processingCancellation = new();
    private bool _disposed;

    internal StorageDeleteNode(
        StorageDeleteOptions options,
        IStorageStoreHandle store,
        TimeProvider clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

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
            _processingCancellation.Dispose();
        }
    }

    private async Task DeleteAsync(StorageDeleteRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

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

        // Borrow the store the storage.store node opened. The delete node never opens
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
                    _store.StoreName,
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
                _store.StoreName,
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
                _store.StoreName,
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
