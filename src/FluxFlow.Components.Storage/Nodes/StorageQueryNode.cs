using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Nodes;

public sealed class StorageQueryNode : FlowNodeBase, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly StorageQueryOptions _options;
    private readonly StorageComponentOptions _componentOptions;
    private readonly StorageStoreContext _storeContext;
    private readonly ActionBlock<StorageQueryRequest> _input;
    private readonly BufferBlock<StorageQueryResult> _result;
    private readonly BufferBlock<StorageRecord> _records;
    private readonly CancellationTokenSource _processingCancellation = new();
    private StorageStoreLease? _lease;
    private bool _startRequested;
    private bool _disposed;

    internal StorageQueryNode(
        StorageQueryOptions options,
        StorageComponentOptions componentOptions,
        StorageStoreContext storeContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _componentOptions = componentOptions ?? throw new ArgumentNullException(nameof(componentOptions));
        _storeContext = storeContext ?? throw new ArgumentNullException(nameof(storeContext));

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

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (_startRequested)
            {
                throw new InvalidOperationException("storage.query node has already started.");
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
            if (_lease is not null)
            {
                await _lease.DisposeAsync().ConfigureAwait(false);
            }

            _processingCancellation.Dispose();
        }
    }

    private async Task QueryAsync(StorageQueryRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var store = _lease?.Store;
        if (store is null)
        {
            ReportError(
                StorageErrorCodes.NotStarted,
                "storage.query has not started.",
                input,
                exception: null);
            return;
        }

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
            return;
        }

        try
        {
            var records = await store.QueryAsync(
                request,
                _processingCancellation.Token).ConfigureAwait(false);
            var copiedRecords = records
                .Select(record => ValidateAndCopyRecord(record, request))
                .Take(request.Limit!.Value)
                .ToArray();

            await _result.SendAsync(
                CreateResult(request, copiedRecords),
                _processingCancellation.Token).ConfigureAwait(false);

            if (_options.EmitRecordOutputs)
            {
                foreach (var record in copiedRecords)
                {
                    await _records.SendAsync(
                        record,
                        _processingCancellation.Token).ConfigureAwait(false);
                }
            }

            TryEmitDiagnostic(
                StorageDiagnosticNames.QueryCompleted,
                message: "storage.query completed.",
                attributes: StorageNodeSupport.CreateCollectionAttributes(
                    "query",
                    _options.Store,
                    request.Collection!,
                    request.CorrelationId,
                    copiedRecords.Length,
                    request.Limit));
        }
        catch (OperationCanceledException) when (_processingCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.QueryFailed,
                $"storage.query failed: {exception.Message}",
                request,
                exception);
        }
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

    private StorageQueryResult CreateResult(
        StorageQueryRequest request,
        IReadOnlyList<StorageRecord> records)
        => new()
        {
            Timestamp = _componentOptions.Clock.GetUtcNow(),
            Operation = "query",
            Collection = request.Collection!,
            Succeeded = true,
            Count = records.Count,
            Records = _options.EmitRecordsInResult ? records.ToArray() : [],
            CorrelationId = request.CorrelationId
        };

    private static StorageRecord ValidateAndCopyRecord(
        StorageRecord record,
        StorageQueryRequest request)
    {
        if (!StringComparer.Ordinal.Equals(record.Collection, request.Collection))
        {
            throw new InvalidOperationException(
                "storage.query store returned a record for a different collection.");
        }

        return StorageNodeSupport.CopyRecord(record);
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
                _options.Store,
                collection,
                correlationId));
        TryEmitDiagnostic(
            StorageDiagnosticNames.QueryFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            StorageNodeSupport.CreateCollectionAttributes(
                "query",
                _options.Store,
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
