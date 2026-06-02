using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

public sealed class FlowCorrelationNode<TInput> : FlowNodeBase
{
    private readonly CorrelationRoutingOptions _options;
    private readonly IFlowExpressionEngine _expressionEngine;
    private readonly IRoutingContextFactory _contextFactory;
    private readonly RoutingNodeContext _nodeContext;
    private readonly Dictionary<string, PendingPair> _pending;
    private readonly StringComparer _comparer;
    private readonly string _requestSide;
    private readonly string _responseSide;
    private readonly TimeSpan _timeout;
    private readonly List<IDataflowBlock> _inputBlocks = [];
    private readonly ActionBlock<CorrelationInput> _processor;
    private readonly TransformBlock<TInput, CorrelationInput>? _input;
    private readonly TransformBlock<TInput, CorrelationInput>? _request;
    private readonly TransformBlock<TInput, CorrelationInput>? _response;
    private readonly BufferBlock<FlowCorrelationMatch<TInput>> _matched;
    private readonly BufferBlock<FlowCorrelationTimeout<TInput>> _timeouts;
    private readonly CancellationToken _processingCancellationToken;

    public FlowCorrelationNode(
        CorrelationRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        IRoutingContextFactory contextFactory,
        RoutingNodeContext nodeContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _expressionEngine = expressionEngine ?? throw new ArgumentNullException(nameof(expressionEngine));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _nodeContext = nodeContext ?? throw new ArgumentNullException(nameof(nodeContext));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.KeyExpression);
        if (options.TimeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.correlation timeout must be greater than zero.");
        }

        if (options.MaxPending <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.correlation max pending count must be greater than zero.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.correlation bounded capacity must be greater than zero.");
        }

        _comparer = options.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        _requestSide = options.RequestSide.Trim();
        _responseSide = options.ResponseSide.Trim();
        if (_comparer.Equals(_requestSide, _responseSide))
        {
            throw new ArgumentException(
                "flow.correlation request side and response side must be different.",
                nameof(options));
        }

        _timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds);
        _pending = new Dictionary<string, PendingPair>(_comparer);
        var outputCapacity = Math.Max(options.BoundedCapacity, options.MaxPending);
        var blockOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacity };
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _processingCancellationToken = inputOptions.CancellationToken;
        _processor = new ActionBlock<CorrelationInput>(CorrelateAsync, inputOptions);
        if (options.UsesSideExpression)
        {
            _input = CreateInputBlock(value => new CorrelationInput(value, Side: null));
        }
        else
        {
            _request = CreateInputBlock(value => new CorrelationInput(value, _requestSide));
            _response = CreateInputBlock(value => new CorrelationInput(value, _responseSide));
        }

        _matched = new BufferBlock<FlowCorrelationMatch<TInput>>(blockOptions);
        _timeouts = new BufferBlock<FlowCorrelationTimeout<TInput>>(blockOptions);
        Task.WhenAll(_inputBlocks.Select(block => block.Completion)).ContinueWith(
            completion => CompleteProcessor(completion),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _processor.Completion.ContinueWith(
            completion => _ = CompleteOutputsAsync(completion),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_matched.Completion, _timeouts.Completion));
    }

    public ITargetBlock<TInput>? Input => _input;

    public ITargetBlock<TInput>? Request => _request;

    public ITargetBlock<TInput>? Response => _response;

    public ISourceBlock<FlowCorrelationMatch<TInput>> Matched => _matched;

    public ISourceBlock<FlowCorrelationTimeout<TInput>> Timeouts => _timeouts;

    public override void Complete()
    {
        foreach (var inputBlock in _inputBlocks)
        {
            inputBlock.Complete();
        }
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            foreach (var inputBlock in _inputBlocks)
            {
                inputBlock.Fault(exception);
            }

            ((IDataflowBlock)_processor).Fault(exception);
            ((IDataflowBlock)_matched).Fault(exception);
            ((IDataflowBlock)_timeouts).Fault(exception);
        }
    }

    private TransformBlock<TInput, CorrelationInput> CreateInputBlock(
        Func<TInput, CorrelationInput> transform)
    {
        var input = new TransformBlock<TInput, CorrelationInput>(
            transform,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        input.LinkTo(_processor, new DataflowLinkOptions { PropagateCompletion = false });
        _inputBlocks.Add(input);
        return input;
    }

    private void CompleteProcessor(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_processor).Fault(exception);
            return;
        }

        if (completion.IsCanceled)
        {
            ((IDataflowBlock)_processor).Fault(new OperationCanceledException());
            return;
        }

        _processor.Complete();
    }

    private async Task CorrelateAsync(CorrelationInput input)
    {
        try
        {
            _processingCancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            await EmitExpiredAsync(now, force: false, _processingCancellationToken).ConfigureAwait(false);
            var item = Evaluate(input);
            if (!TryNormalizeSide(item.Side, out var side))
            {
                ReportCorrelationError(
                    RoutingErrorCodes.CorrelationInvalidSide,
                    $"flow.correlation side '{item.Side}' is not supported.",
                    null,
                    item.Key,
                    item.Side);
                return;
            }

            if (!TryGetOrCreatePending(item.Key, out var pending))
            {
                ReportCorrelationError(
                    RoutingErrorCodes.CorrelationCapacityExceeded,
                    $"flow.correlation maxPending limit reached; key '{item.Key}' was not tracked.",
                    null,
                    item.Key,
                    side);
                return;
            }

            var entry = new PendingEntry(input.Value, side, now);
            if (pending.HasSide(side, _comparer))
            {
                ReportCorrelationError(
                    RoutingErrorCodes.CorrelationDuplicateSide,
                    $"flow.correlation replaced duplicate side '{side}' for key '{item.Key}'.",
                    null,
                    item.Key,
                    side);
            }

            pending.Set(side, entry, _requestSide, _comparer);
            if (pending.Request is null || pending.Response is null)
            {
                return;
            }

            _pending.Remove(item.Key);
            await EmitMatchAsync(item.Key, pending.Request, pending.Response, now).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_processingCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CorrelationException exception)
        {
            ReportCorrelationError(
                exception.Code,
                exception.Message,
                exception.InnerException,
                exception.Key,
                exception.Side);
        }
        catch (Exception exception)
        {
            ReportCorrelationError(
                RoutingErrorCodes.CorrelationKeyFailed,
                $"flow.correlation failed: {exception.Message}",
                exception);
        }
    }

    private CorrelationItem Evaluate(CorrelationInput input)
    {
        string? key;
        try
        {
            key = CorrelationNodeSupport.EvaluateKey(
                _expressionEngine,
                _options,
                _contextFactory,
                _nodeContext,
                input.Value);
        }
        catch (Exception exception)
        {
            throw new CorrelationException(
                RoutingErrorCodes.CorrelationKeyFailed,
                $"flow.correlation failed to evaluate key: {exception.Message}",
                exception);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new CorrelationException(
                RoutingErrorCodes.CorrelationInvalidKey,
                "flow.correlation key cannot be empty.");
        }

        var side = input.Side;
        if (string.IsNullOrWhiteSpace(side))
        {
            try
            {
                side = CorrelationNodeSupport.EvaluateSide(
                    _expressionEngine,
                    _options,
                    _contextFactory,
                    _nodeContext,
                    input.Value);
            }
            catch (Exception exception)
            {
                throw new CorrelationException(
                    RoutingErrorCodes.CorrelationSideFailed,
                    $"flow.correlation failed to evaluate side: {exception.Message}",
                    exception,
                    key);
            }
        }

        if (string.IsNullOrWhiteSpace(side))
        {
            throw new CorrelationException(
                RoutingErrorCodes.CorrelationInvalidSide,
                "flow.correlation side cannot be empty.",
                key: key);
        }

        return new CorrelationItem(key, side);
    }

    private bool TryNormalizeSide(string side, out string normalized)
    {
        if (_comparer.Equals(side, _requestSide))
        {
            normalized = _requestSide;
            return true;
        }

        if (_comparer.Equals(side, _responseSide))
        {
            normalized = _responseSide;
            return true;
        }

        normalized = side;
        return false;
    }

    private bool TryGetOrCreatePending(
        string key,
        out PendingPair pending)
    {
        if (_pending.TryGetValue(key, out pending!))
        {
            return true;
        }

        if (_pending.Count >= _options.MaxPending)
        {
            pending = default!;
            return false;
        }

        pending = new PendingPair();
        _pending[key] = pending;
        return true;
    }

    private async Task EmitExpiredAsync(
        DateTimeOffset now,
        bool force,
        CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        foreach (var (key, pending) in _pending.ToArray())
        {
            var entries = pending.Entries
                .Where(entry => force || now - entry.ReceivedAt >= _timeout)
                .ToArray();
            if (entries.Length == 0)
            {
                continue;
            }

            _pending.Remove(key);
            foreach (var entry in entries)
            {
                await EmitTimeoutAsync(key, entry, now, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task EmitMatchAsync(
        string key,
        PendingEntry request,
        PendingEntry response,
        DateTimeOffset now)
    {
        var match = new FlowCorrelationMatch<TInput>
        {
            Key = key,
            Request = request.Value,
            Response = response.Value,
            RequestReceivedAt = request.ReceivedAt,
            ResponseReceivedAt = response.ReceivedAt,
            MatchedAt = now,
            Elapsed = now - (request.ReceivedAt <= response.ReceivedAt
                ? request.ReceivedAt
                : response.ReceivedAt)
        };
        await _matched.SendAsync(match, _processingCancellationToken).ConfigureAwait(false);
        TryEmitDiagnostic(
            RoutingDiagnosticNames.CorrelationMatched,
            message: "flow.correlation matched pair.",
            attributes: CorrelationNodeSupport.CreateAttributes(
                _options,
                _expressionEngine,
                _pending.Count,
                key));
    }

    private async Task EmitTimeoutAsync(
        string key,
        PendingEntry entry,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var timeout = new FlowCorrelationTimeout<TInput>
        {
            Key = key,
            Side = entry.Side,
            Value = entry.Value,
            ReceivedAt = entry.ReceivedAt,
            TimedOutAt = now,
            Timeout = _timeout
        };
        await _timeouts.SendAsync(timeout, cancellationToken).ConfigureAwait(false);
        TryEmitDiagnostic(
            RoutingDiagnosticNames.CorrelationTimedOut,
            FlowDiagnosticLevel.Warning,
            "flow.correlation emitted timeout.",
            attributes: CorrelationNodeSupport.CreateAttributes(
                _options,
                _expressionEngine,
                _pending.Count,
                key,
                entry.Side));
    }

    private async Task CompleteOutputsAsync(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } completionException)
        {
            ((IDataflowBlock)_matched).Fault(completionException);
            ((IDataflowBlock)_timeouts).Fault(completionException);
            return;
        }

        try
        {
            await EmitExpiredAsync(DateTimeOffset.UtcNow, force: true, CancellationToken.None)
                .ConfigureAwait(false);
            _matched.Complete();
            _timeouts.Complete();
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_matched).Fault(exception);
            ((IDataflowBlock)_timeouts).Fault(exception);
        }
    }

    private void ReportCorrelationError(
        int code,
        string message,
        Exception? exception,
        string? key = null,
        string? side = null)
    {
        TryReportError(
            code,
            message,
            exception,
            CorrelationNodeSupport.CreateErrorContext(_options, _expressionEngine, key, side));
        TryEmitDiagnostic(
            RoutingDiagnosticNames.CorrelationFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CorrelationNodeSupport.CreateAttributes(
                _options,
                _expressionEngine,
                _pending.Count,
                key,
                side));
    }

    private sealed record CorrelationItem(
        string Key,
        string Side);

    private sealed record CorrelationInput(
        TInput Value,
        string? Side);

    private sealed record PendingEntry(
        TInput Value,
        string Side,
        DateTimeOffset ReceivedAt);

    private sealed class PendingPair
    {
        public PendingEntry? Request { get; private set; }
        public PendingEntry? Response { get; private set; }

        public IEnumerable<PendingEntry> Entries
        {
            get
            {
                if (Request is not null)
                {
                    yield return Request;
                }

                if (Response is not null)
                {
                    yield return Response;
                }
            }
        }

        public bool HasSide(string side, StringComparer comparer)
            => Request is not null && comparer.Equals(Request.Side, side)
               || Response is not null && comparer.Equals(Response.Side, side);

        public void Set(
            string side,
            PendingEntry entry,
            string requestSide,
            StringComparer comparer)
        {
            if (comparer.Equals(side, requestSide))
            {
                Request = entry;
                return;
            }

            Response = entry;
        }
    }

    private sealed class CorrelationException(
        int code,
        string message,
        Exception? innerException = null,
        string? key = null,
        string? side = null)
        : Exception(message, innerException)
    {
        public int Code { get; } = code;
        public string? Key { get; } = key;
        public string? Side { get; } = side;
    }
}
