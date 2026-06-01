using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Diagnostics;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Storage.Nodes;

public sealed class StoragePutNode : FlowNodeBase, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly StoragePutOptions _options;
    private readonly StorageComponentOptions _componentOptions;
    private readonly StorageStoreContext _storeContext;
    private readonly ActionBlock<StoragePutRequest> _input;
    private readonly BufferBlock<StorageResult> _result;
    private readonly CancellationTokenSource _processingCancellation = new();
    private StorageStoreLease? _lease;
    private bool _startRequested;
    private bool _disposed;

    private StoragePutNode(
        StoragePutOptions options,
        StorageComponentOptions componentOptions,
        StorageStoreContext storeContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _componentOptions = componentOptions ?? throw new ArgumentNullException(nameof(componentOptions));
        _storeContext = storeContext ?? throw new ArgumentNullException(nameof(storeContext));

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

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        StorageComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = StorageOptionsReader.ReadPutOptions(context.Definition);
        var node = new StoragePutNode(
            options,
            componentOptions,
            StorageNodeSupport.CreateStoreContext(
                context.Address,
                StorageComponentTypes.Put,
                options.Store,
                options.Collection));

        return context.CreateNode(node)
            .Input(StorageComponentPorts.Input, node.Input)
            .Output(StorageComponentPorts.Result, node.Result)
            .Output(StorageComponentPorts.Errors, node.Errors)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock)
        {
            if (_startRequested)
            {
                throw new InvalidOperationException("storage.put node has already started.");
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

    private async Task PutAsync(StoragePutRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var store = _lease?.Store;
        if (store is null)
        {
            ReportError(
                StorageErrorCodes.NotStarted,
                "storage.put has not started.",
                input,
                exception: null);
            return;
        }

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
            return;
        }

        try
        {
            var record = await store.PutAsync(
                request,
                _processingCancellation.Token).ConfigureAwait(false);
            ValidateRecord(record, request);
            var result = StorageNodeSupport.CreateRecordResult(
                "put",
                record,
                _options.EmitStoredRecord,
                request.CorrelationId);

            await _result.SendAsync(result, _processingCancellation.Token).ConfigureAwait(false);
            TryEmitDiagnostic(
                StorageDiagnosticNames.PutStored,
                message: "storage.put stored record.",
                attributes: StorageNodeSupport.CreateOperationAttributes(
                    "put",
                    _options.Store,
                    request.Collection!,
                    request.Key,
                    request.CorrelationId,
                    record.Version));
        }
        catch (OperationCanceledException) when (_processingCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportError(
                StorageErrorCodes.PutFailed,
                $"storage.put failed: {exception.Message}",
                request,
                exception);
        }
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
                _options.Store,
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
                _options.Store,
                collection,
                key,
                correlationId));
    }

    private static void ValidateRecord(StorageRecord record, StoragePutRequest request)
    {
        if (!StringComparer.Ordinal.Equals(record.Collection, request.Collection))
        {
            throw new InvalidOperationException(
                "storage.put store returned a record for a different collection.");
        }

        if (!StringComparer.Ordinal.Equals(record.Key, request.Key))
        {
            throw new InvalidOperationException(
                "storage.put store returned a record for a different key.");
        }
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
