using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Nodes;

public sealed class StoragePutNode : FlowNodeBase, IAsyncDisposable
{
    private const string NotAvailableMessage =
        "storage.put is not available; the storage.store component does not open a store yet.";

    private readonly StoragePutOptions _options;
    private readonly IStorageStoreHandle _store;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<StoragePutRequest> _input;
    private readonly BufferBlock<StorageResult> _result;
    private readonly CancellationTokenSource _processingCancellation = new();
    private bool _disposed;

    internal StoragePutNode(
        StoragePutOptions options,
        IStorageStoreHandle store,
        TimeProvider clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        var executionOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _input = new ActionBlock<StoragePutRequest>(PutAsync, executionOptions);
        _result = new BufferBlock<StorageResult>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        _input.Completion.ContinueWith(
            CompleteOutput,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(_result.Completion);
    }

    public ITargetBlock<StoragePutRequest> Input => _input;

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

    private Task PutAsync(StoragePutRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

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

    private void ReportError(
        int code,
        string message,
        StoragePutRequest input,
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
                "put",
                _store.StoreName,
                collection,
                key,
                correlationId));
        TryEmitDiagnostic(
            StorageDiagnosticNames.PutFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            StorageNodeSupport.CreateOperationAttributes(
                "put",
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
