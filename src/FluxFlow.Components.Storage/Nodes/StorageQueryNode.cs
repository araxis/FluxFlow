using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Nodes;

public sealed class StorageQueryNode : FlowNodeBase, IAsyncDisposable
{
    private const string NotAvailableMessage =
        "storage.query is not available; the storage.store component does not open a store yet.";

    private readonly StorageQueryOptions _options;
    private readonly IStorageStoreHandle _store;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<StorageQueryRequest> _input;
    private readonly BufferBlock<StorageQueryResult> _result;
    private readonly BufferBlock<StorageRecord> _records;
    private readonly CancellationTokenSource _processingCancellation = new();
    private bool _disposed;

    internal StorageQueryNode(
        StorageQueryOptions options,
        IStorageStoreHandle store,
        TimeProvider clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };
        _input = new ActionBlock<StorageQueryRequest>(
            QueryAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _result = new BufferBlock<StorageQueryResult>(blockOptions);
        _records = new BufferBlock<StorageRecord>(blockOptions);
        _input.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_result.Completion, _records.Completion));
    }

    public ITargetBlock<StorageQueryRequest> Input => _input;

    public ISourceBlock<StorageQueryResult> Result => _result;

    public ISourceBlock<StorageRecord> Records => _records;

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
            ((IDataflowBlock)_records).Fault(exception);
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

    private Task QueryAsync(StorageQueryRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        StorageQueryRequest request;
        try
        {
            request = NormalizeRequest(input);
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.InvalidRequest,
                $"storage.query request is invalid: {exception.Message}",
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

    private StorageQueryRequest NormalizeRequest(StorageQueryRequest input)
    {
        var request = input with
        {
            Collection = StorageNodeSupport.ResolveCollection(
                "storage.query",
                input.Collection,
                _options.Collection),
            KeyPrefix = StorageNodeSupport.Normalize(input.KeyPrefix),
            Attributes = StorageNodeSupport.CopyAttributes(input.Attributes),
            IncludeExpired = input.IncludeExpired ?? _options.IncludeExpired,
            Offset = input.Offset ?? _options.Offset,
            Limit = input.Limit ?? _options.Limit,
            CorrelationId = StorageNodeSupport.Normalize(input.CorrelationId)
        };
        StorageQueryMatcher.Validate(request);
        return request;
    }

    private void ReportError(
        int code,
        string message,
        StorageQueryRequest input,
        Exception? exception)
    {
        var collection = StorageNodeSupport.Normalize(input.Collection)
            ?? StorageNodeSupport.Normalize(_options.Collection)
            ?? "(missing)";
        var correlationId = StorageNodeSupport.Normalize(input.CorrelationId);
        TryReportError(
            code,
            message,
            exception,
            StorageNodeSupport.CreateCollectionContext(
                "query",
                _store.StoreName,
                collection,
                correlationId));
        TryEmitDiagnostic(
            StorageDiagnosticNames.QueryFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            StorageNodeSupport.CreateCollectionAttributes(
                "query",
                _store.StoreName,
                collection,
                correlationId));
    }

    private void CompleteOutputs(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_result).Fault(exception);
            ((IDataflowBlock)_records).Fault(exception);
            return;
        }

        _result.Complete();
        _records.Complete();
    }
}
