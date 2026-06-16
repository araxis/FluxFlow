using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Nodes;

public sealed class StorageDeleteNode : FlowNodeBase, IAsyncDisposable
{
    private const string NotAvailableMessage =
        "storage.delete is not available; the storage.store component does not open a store yet.";

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

    private Task DeleteAsync(StorageDeleteRequest input)
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
            return Task.CompletedTask;
        }

        // The storage.store component holds configuration only; no store is
        // opened yet, so every otherwise-valid request reports not available.
        ReportError(
            StorageErrorCodes.StoreNotAvailable,
            NotAvailableMessage,
            request,
            exception: null);
        return Task.CompletedTask;
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
