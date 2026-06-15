using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Components.Routing.Timing;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

public sealed class FlowCorrelationNode<TInput> : FlowNodeBase, IAsyncDisposable
{
    private readonly CorrelationRoutingOptions _options;
    private readonly Func<TInput, string?> _keySelector;
    private readonly Func<TInput, string?>? _sideSelector;
    private readonly string? _engineName;
    private readonly IRoutingClock _clock;
    private readonly Dictionary<string, PendingPair> _pending;
    private readonly Queue<CorrelationDeadline> _deadlines = new();
    private readonly StringComparer _comparer;
    private readonly string _requestSide;
    private readonly string _responseSide;
    private readonly TimeSpan _timeout;
    private readonly List<IDataflowBlock> _inputBlocks = [];
    private readonly ActionBlock<CorrelationCommand> _processor;
    private readonly TransformBlock<TInput, CorrelationCommand>? _input;
    private readonly TransformBlock<TInput, CorrelationCommand>? _request;
    private readonly TransformBlock<TInput, CorrelationCommand>? _response;
    private readonly BufferBlock<FlowCorrelationMatch<TInput>> _matched;
    private readonly BufferBlock<FlowCorrelationTimeout<TInput>> _timeouts;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private volatile CancellationTokenSource? _timerCancellation;
    private long _timerVersion;
    private bool _disposed;

    public FlowCorrelationNode(
        CorrelationRoutingOptions options,
        Func<TInput, string?> keySelector,
        Func<TInput, string?>? sideSelector,
        string? engineName)
        : this(
            options,
            keySelector,
            sideSelector,
            SystemRoutingClock.Instance,
            engineName)
    {
    }

    public FlowCorrelationNode(
        CorrelationRoutingOptions options,
        Func<TInput, string?> keySelector,
        Func<TInput, string?>? sideSelector,
        IRoutingClock clock,
        string? engineName)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _sideSelector = sideSelector;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _engineName = engineName;
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
        _processor = new ActionBlock<CorrelationCommand>(ProcessCommandAsync, inputOptions);
        if (options.UsesSideExpression)
        {
            _input = CreateInputBlock(value => CorrelationCommand.FromInput(
                new CorrelationInput(value, Side: null)));
        }
        else
        {
            _request = CreateInputBlock(value => CorrelationCommand.FromInput(
                new CorrelationInput(value, _requestSide)));
            _response = CreateInputBlock(value => CorrelationCommand.FromInput(
                new CorrelationInput(value, _responseSide)));
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
            _lifecycleCancellation.Cancel();
            TryCancelTimer();
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Complete();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Dispose must not throw when the node faulted.
        }
        finally
        {
            _timerCancellation?.Dispose();
            _lifecycleCancellation.Dispose();
        }
    }

    private TransformBlock<TInput, CorrelationCommand> CreateInputBlock(
        Func<TInput, CorrelationCommand> transform)
    {
        var input = new TransformBlock<TInput, CorrelationCommand>(
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

    private async Task ProcessCommandAsync(CorrelationCommand command)
    {
        try
        {
            switch (command.Kind)
            {
                case CorrelationCommandKind.Input:
                    await CorrelateAsync(command.Input!).ConfigureAwait(false);
                    break;
                case CorrelationCommandKind.Timer:
                    await ExpireByTimerAsync(command.TimerVersion).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"flow.correlation command '{command.Kind}' is not supported.");
            }
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
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

    private async Task CorrelateAsync(CorrelationInput input)
    {
        try
        {
            var now = _clock.UtcNow;
            await EmitExpiredAsync(now, force: false, _lifecycleCancellation.Token).ConfigureAwait(false);
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

            if (!TryGetOrCreatePending(item.Key, out var pending, out var created))
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
            if (pending.Get(side, _comparer) is { } existing)
            {
                entry = entry with { ReceivedAt = existing.ReceivedAt };
                TryEmitDiagnostic(
                    RoutingDiagnosticNames.CorrelationDuplicateSide,
                    FlowDiagnosticLevel.Warning,
                    $"flow.correlation replaced duplicate side '{side}' for key '{item.Key}'.",
                    attributes: CorrelationNodeSupport.CreateAttributes(
                        _options,
                        _engineName,
                        _pending.Count,
                        item.Key,
                        side));
            }

            pending.Set(side, entry, _requestSide, _comparer);
            if (created)
            {
                _deadlines.Enqueue(new CorrelationDeadline(item.Key, entry.ReceivedAt));
            }

            if (pending.Request is null || pending.Response is null)
            {
                return;
            }

            _pending.Remove(item.Key);
            await EmitMatchAsync(item.Key, pending.Request, pending.Response, now).ConfigureAwait(false);
        }
        finally
        {
            ScheduleTimer(_clock.UtcNow);
        }
    }

    private async Task ExpireByTimerAsync(long version)
    {
        if (version != _timerVersion)
        {
            return;
        }

        await EmitExpiredAsync(_clock.UtcNow, force: false, _lifecycleCancellation.Token)
            .ConfigureAwait(false);
        ScheduleTimer(_clock.UtcNow);
    }

    private CorrelationItem Evaluate(CorrelationInput input)
    {
        string? key;
        try
        {
            key = _keySelector(input.Value);
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
        if (string.IsNullOrWhiteSpace(side) && _sideSelector is not null)
        {
            try
            {
                side = _sideSelector(input.Value);
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
        out PendingPair pending,
        out bool created)
    {
        created = false;
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
        created = true;
        return true;
    }

    private async Task EmitExpiredAsync(
        DateTimeOffset now,
        bool force,
        CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            _deadlines.Clear();
            return;
        }

        while (_deadlines.Count > 0)
        {
            var deadline = _deadlines.Peek();
            if (!_pending.TryGetValue(deadline.Key, out var pending)
                || pending.ReceivedAt != deadline.ReceivedAt)
            {
                _deadlines.Dequeue();
                continue;
            }

            if (!force && now - deadline.ReceivedAt < _timeout)
            {
                return;
            }

            _deadlines.Dequeue();
            _pending.Remove(deadline.Key);
            foreach (var entry in pending.Entries)
            {
                await EmitTimeoutAsync(deadline.Key, entry, now, cancellationToken).ConfigureAwait(false);
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
        await _matched.SendAsync(match, _lifecycleCancellation.Token).ConfigureAwait(false);
        TryEmitDiagnostic(
            RoutingDiagnosticNames.CorrelationMatched,
            message: "flow.correlation matched pair.",
            attributes: CorrelationNodeSupport.CreateAttributes(
                _options,
                _engineName,
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
                _engineName,
                _pending.Count,
                key,
                entry.Side));
    }

    private async Task CompleteOutputsAsync(Task completion)
    {
        TryCancelTimer();
        if (completion.IsFaulted && completion.Exception is { } completionException)
        {
            ((IDataflowBlock)_matched).Fault(completionException);
            ((IDataflowBlock)_timeouts).Fault(completionException);
            return;
        }

        try
        {
            await EmitExpiredAsync(_clock.UtcNow, force: true, CancellationToken.None)
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

    private void ScheduleTimer(DateTimeOffset now)
    {
        var previous = Interlocked.Exchange(ref _timerCancellation, null);
        previous?.Cancel();
        previous?.Dispose();
        _timerVersion++;
        if (_pending.Count == 0)
        {
            return;
        }

        var dueAt = GetNextDueAt();
        if (dueAt is null)
        {
            return;
        }

        var delay = dueAt.Value <= now
            ? TimeSpan.Zero
            : dueAt.Value - now;
        var version = _timerVersion;
        var timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifecycleCancellation.Token);
        _timerCancellation = timerCancellation;
        _ = RunTimerAsync(version, delay, timerCancellation.Token);
    }

    private DateTimeOffset? GetNextDueAt()
    {
        while (_deadlines.Count > 0)
        {
            var deadline = _deadlines.Peek();
            if (_pending.TryGetValue(deadline.Key, out var pending)
                && pending.ReceivedAt == deadline.ReceivedAt)
            {
                return deadline.ReceivedAt + _timeout;
            }

            _deadlines.Dequeue();
        }

        return null;
    }

    private void TryCancelTimer()
    {
        var timerCancellation = Interlocked.Exchange(ref _timerCancellation, null);
        timerCancellation?.Cancel();
        timerCancellation?.Dispose();
    }

    private async Task RunTimerAsync(
        long version,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await _clock.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
            await _processor.SendAsync(
                CorrelationCommand.Timer(version),
                _lifecycleCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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
            CorrelationNodeSupport.CreateErrorContext(_options, _engineName, key, side));
        TryEmitDiagnostic(
            RoutingDiagnosticNames.CorrelationFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CorrelationNodeSupport.CreateAttributes(
                _options,
                _engineName,
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

    private sealed record CorrelationCommand(
        CorrelationCommandKind Kind,
        CorrelationInput? Input = null,
        long TimerVersion = 0)
    {
        public static CorrelationCommand FromInput(CorrelationInput input)
            => new(CorrelationCommandKind.Input, input);

        public static CorrelationCommand Timer(long version)
            => new(CorrelationCommandKind.Timer, TimerVersion: version);
    }

    private enum CorrelationCommandKind
    {
        Input,
        Timer
    }

    private sealed record CorrelationDeadline(
        string Key,
        DateTimeOffset ReceivedAt);

    private sealed record PendingEntry(
        TInput Value,
        string Side,
        DateTimeOffset ReceivedAt);

    private sealed class PendingPair
    {
        public PendingEntry? Request { get; private set; }
        public PendingEntry? Response { get; private set; }

        public DateTimeOffset? ReceivedAt
            => Request?.ReceivedAt ?? Response?.ReceivedAt;

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

        public PendingEntry? Get(string side, StringComparer comparer)
            => Request is not null && comparer.Equals(Request.Side, side)
                ? Request
                : Response is not null && comparer.Equals(Response.Side, side)
                    ? Response
                    : null;

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
