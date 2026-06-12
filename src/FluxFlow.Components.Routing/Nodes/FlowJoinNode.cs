using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Components.Routing.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

public sealed class FlowJoinNode<TLeft, TRight> : FlowNodeBase, IAsyncDisposable
{
    private readonly JoinRoutingOptions _options;
    private readonly IFlowExpressionEngine _expressionEngine;
    private readonly IRoutingContextFactory _leftContextFactory;
    private readonly IRoutingContextFactory _rightContextFactory;
    private readonly RoutingNodeContext _leftNodeContext;
    private readonly RoutingNodeContext _rightNodeContext;
    private readonly IRoutingClock _clock;
    private readonly Dictionary<string, PendingBucket> _pending;
    private readonly Queue<JoinDeadline> _deadlines = new();
    private readonly TimeSpan _timeout;
    private readonly ActionBlock<TLeft> _left;
    private readonly ActionBlock<TRight> _right;
    private readonly ActionBlock<JoinCommand> _commands;
    private readonly BufferBlock<FlowJoinResult<TLeft, TRight>> _output;
    private readonly BufferBlock<FlowJoinTimeout<TLeft, TRight>> _timeouts;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private volatile CancellationTokenSource? _timerCancellation;
    private long _timerVersion;
    private int _pendingCount;
    private bool _leftCompleted;
    private bool _rightCompleted;
    private bool _disposed;

    public FlowJoinNode(
        JoinRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        IRoutingContextFactory leftContextFactory,
        IRoutingContextFactory rightContextFactory,
        RoutingNodeContext leftNodeContext,
        RoutingNodeContext rightNodeContext)
        : this(
            options,
            expressionEngine,
            leftContextFactory,
            rightContextFactory,
            leftNodeContext,
            rightNodeContext,
            SystemRoutingClock.Instance)
    {
    }

    public FlowJoinNode(
        JoinRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        IRoutingContextFactory leftContextFactory,
        IRoutingContextFactory rightContextFactory,
        RoutingNodeContext leftNodeContext,
        RoutingNodeContext rightNodeContext,
        IRoutingClock clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _expressionEngine = expressionEngine ?? throw new ArgumentNullException(nameof(expressionEngine));
        _leftContextFactory = leftContextFactory ?? throw new ArgumentNullException(nameof(leftContextFactory));
        _rightContextFactory = rightContextFactory ?? throw new ArgumentNullException(nameof(rightContextFactory));
        _leftNodeContext = leftNodeContext ?? throw new ArgumentNullException(nameof(leftNodeContext));
        _rightNodeContext = rightNodeContext ?? throw new ArgumentNullException(nameof(rightNodeContext));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LeftKeyExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RightKeyExpression);
        if (options.TimeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.join timeout must be greater than zero.");
        }

        if (options.MaxPending <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.join max pending count must be greater than zero.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.join bounded capacity must be greater than zero.");
        }

        var comparer = options.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        _pending = new Dictionary<string, PendingBucket>(comparer);
        _timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds);
        var outputCapacity = Math.Max(options.BoundedCapacity, options.MaxPending);
        var blockOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacity };
        var executionOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };

        _output = new BufferBlock<FlowJoinResult<TLeft, TRight>>(blockOptions);
        _timeouts = new BufferBlock<FlowJoinTimeout<TLeft, TRight>>(blockOptions);
        _commands = new ActionBlock<JoinCommand>(ProcessCommandAsync, executionOptions);
        _left = new ActionBlock<TLeft>(
            async input => await _commands.SendAsync(
                JoinCommand.FromLeft(input),
                _lifecycleCancellation.Token).ConfigureAwait(false),
            executionOptions);
        _right = new ActionBlock<TRight>(
            async input => await _commands.SendAsync(
                JoinCommand.FromRight(input),
                _lifecycleCancellation.Token).ConfigureAwait(false),
            executionOptions);
        _left.Completion.ContinueWith(
            completion => _ = CompleteSideAsync(FlowJoinSide.Left, completion),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _right.Completion.ContinueWith(
            completion => _ = CompleteSideAsync(FlowJoinSide.Right, completion),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _commands.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_output.Completion, _timeouts.Completion));
    }

    public ITargetBlock<TLeft> Left => _left;

    public ITargetBlock<TRight> Right => _right;

    public ISourceBlock<FlowJoinResult<TLeft, TRight>> Output => _output;

    public ISourceBlock<FlowJoinTimeout<TLeft, TRight>> Timeouts => _timeouts;

    public override void Complete()
    {
        _left.Complete();
        _right.Complete();
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
            ((IDataflowBlock)_left).Fault(exception);
            ((IDataflowBlock)_right).Fault(exception);
            ((IDataflowBlock)_commands).Fault(exception);
            ((IDataflowBlock)_output).Fault(exception);
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

    private async Task ProcessCommandAsync(JoinCommand command)
    {
        try
        {
            switch (command.Kind)
            {
                case JoinCommandKind.Left:
                    await AddLeftAsync(command.Left!).ConfigureAwait(false);
                    break;
                case JoinCommandKind.Right:
                    await AddRightAsync(command.Right!).ConfigureAwait(false);
                    break;
                case JoinCommandKind.Timer:
                    await ExpireByTimerAsync(command.TimerVersion).ConfigureAwait(false);
                    break;
                case JoinCommandKind.CompleteSide:
                    await CompleteSideCoreAsync(command.Side!.Value).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"flow.join command '{command.Kind}' is not supported.");
            }
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (JoinException exception)
        {
            ReportJoinError(
                exception.Code,
                exception.Message,
                exception.InnerException,
                exception.Key,
                exception.Side);
        }
        catch (Exception exception)
        {
            ReportJoinError(
                RoutingErrorCodes.JoinFailed,
                $"flow.join failed: {exception.Message}",
                exception);
            throw;
        }
    }

    private async Task AddLeftAsync(TLeft input)
    {
        var now = _clock.UtcNow;
        await EmitExpiredAsync(now, force: false, _lifecycleCancellation.Token).ConfigureAwait(false);
        var key = EvaluateLeftKey(input);
        await AddLeftCoreAsync(key, input, now).ConfigureAwait(false);
        ScheduleTimer(_clock.UtcNow);
    }

    private async Task AddRightAsync(TRight input)
    {
        var now = _clock.UtcNow;
        await EmitExpiredAsync(now, force: false, _lifecycleCancellation.Token).ConfigureAwait(false);
        var key = EvaluateRightKey(input);
        await AddRightCoreAsync(key, input, now).ConfigureAwait(false);
        ScheduleTimer(_clock.UtcNow);
    }

    private string EvaluateLeftKey(TLeft input)
    {
        string? key;
        try
        {
            key = JoinNodeSupport.EvaluateLeftKey(
                _expressionEngine,
                _options,
                _leftContextFactory,
                _leftNodeContext,
                input);
        }
        catch (Exception exception)
        {
            throw new JoinException(
                RoutingErrorCodes.JoinLeftKeyFailed,
                $"flow.join failed to evaluate left key: {exception.Message}",
                exception,
                side: FlowJoinSide.Left);
        }

        return ValidateKey(key, FlowJoinSide.Left);
    }

    private string EvaluateRightKey(TRight input)
    {
        string? key;
        try
        {
            key = JoinNodeSupport.EvaluateRightKey(
                _expressionEngine,
                _options,
                _rightContextFactory,
                _rightNodeContext,
                input);
        }
        catch (Exception exception)
        {
            throw new JoinException(
                RoutingErrorCodes.JoinRightKeyFailed,
                $"flow.join failed to evaluate right key: {exception.Message}",
                exception,
                side: FlowJoinSide.Right);
        }

        return ValidateKey(key, FlowJoinSide.Right);
    }

    private static string ValidateKey(
        string? key,
        FlowJoinSide side)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new JoinException(
                RoutingErrorCodes.JoinInvalidKey,
                "flow.join key cannot be empty.",
                side: side);
        }

        return key;
    }

    private async Task AddLeftCoreAsync(
        string key,
        TLeft input,
        DateTimeOffset now)
    {
        if (_pending.TryGetValue(key, out var bucket)
            && bucket.Rights.Count > 0)
        {
            var right = bucket.Rights.Dequeue();
            _pendingCount--;
            RemoveIfEmpty(key, bucket);
            await EmitResultAsync(key, new PendingEntry<TLeft>(input, now), right, now)
                .ConfigureAwait(false);
            return;
        }

        if (!CanTrackPending(key, FlowJoinSide.Left))
        {
            return;
        }

        bucket = GetOrCreateBucket(key);
        bucket.Lefts.Enqueue(new PendingEntry<TLeft>(input, now));
        _deadlines.Enqueue(new JoinDeadline(key, FlowJoinSide.Left, now));
        _pendingCount++;
    }

    private async Task AddRightCoreAsync(
        string key,
        TRight input,
        DateTimeOffset now)
    {
        if (_pending.TryGetValue(key, out var bucket)
            && bucket.Lefts.Count > 0)
        {
            var left = bucket.Lefts.Dequeue();
            _pendingCount--;
            RemoveIfEmpty(key, bucket);
            await EmitResultAsync(key, left, new PendingEntry<TRight>(input, now), now)
                .ConfigureAwait(false);
            return;
        }

        if (!CanTrackPending(key, FlowJoinSide.Right))
        {
            return;
        }

        bucket = GetOrCreateBucket(key);
        bucket.Rights.Enqueue(new PendingEntry<TRight>(input, now));
        _deadlines.Enqueue(new JoinDeadline(key, FlowJoinSide.Right, now));
        _pendingCount++;
    }

    private PendingBucket GetOrCreateBucket(string key)
    {
        if (_pending.TryGetValue(key, out var bucket))
        {
            return bucket;
        }

        bucket = new PendingBucket();
        _pending[key] = bucket;
        return bucket;
    }

    private bool CanTrackPending(
        string key,
        FlowJoinSide side)
    {
        if (_pendingCount < _options.MaxPending)
        {
            return true;
        }

        ReportJoinError(
            RoutingErrorCodes.JoinCapacityExceeded,
            $"flow.join maxPending limit reached; key '{key}' was not tracked.",
            null,
            key,
            side);
        return false;
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

    private async Task EmitExpiredAsync(
        DateTimeOffset now,
        bool force,
        CancellationToken cancellationToken)
    {
        if (_pendingCount == 0)
        {
            _deadlines.Clear();
            return;
        }

        while (_deadlines.Count > 0)
        {
            var deadline = _deadlines.Peek();
            if (!TryPeekPendingEntry(deadline, out var bucket))
            {
                _deadlines.Dequeue();
                continue;
            }

            if (!force && now - deadline.ReceivedAt < _timeout)
            {
                return;
            }

            _deadlines.Dequeue();
            _pendingCount--;
            if (deadline.Side == FlowJoinSide.Left)
            {
                var entry = bucket.Lefts.Dequeue();
                RemoveIfEmpty(deadline.Key, bucket);
                await EmitTimeoutAsync(
                    deadline.Key,
                    FlowJoinSide.Left,
                    entry,
                    now,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var entry = bucket.Rights.Dequeue();
                RemoveIfEmpty(deadline.Key, bucket);
                await EmitTimeoutAsync(
                    deadline.Key,
                    FlowJoinSide.Right,
                    entry,
                    now,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private bool TryPeekPendingEntry(
        JoinDeadline deadline,
        out PendingBucket bucket)
    {
        if (!_pending.TryGetValue(deadline.Key, out bucket!))
        {
            return false;
        }

        return deadline.Side == FlowJoinSide.Left
            ? bucket.Lefts.Count > 0 && bucket.Lefts.Peek().ReceivedAt == deadline.ReceivedAt
            : bucket.Rights.Count > 0 && bucket.Rights.Peek().ReceivedAt == deadline.ReceivedAt;
    }

    private async Task EmitResultAsync(
        string key,
        PendingEntry<TLeft> left,
        PendingEntry<TRight> right,
        DateTimeOffset now)
    {
        var result = new FlowJoinResult<TLeft, TRight>
        {
            Key = key,
            Left = left.Value,
            Right = right.Value,
            LeftReceivedAt = left.ReceivedAt,
            RightReceivedAt = right.ReceivedAt,
            JoinedAt = now,
            Elapsed = now - (left.ReceivedAt <= right.ReceivedAt
                ? left.ReceivedAt
                : right.ReceivedAt)
        };

        await _output.SendAsync(result, _lifecycleCancellation.Token).ConfigureAwait(false);
        TryEmitDiagnostic(
            RoutingDiagnosticNames.JoinMatched,
            message: "flow.join matched values.",
            attributes: JoinNodeSupport.CreateAttributes(
                _options,
                _expressionEngine,
                _pendingCount,
                key));
    }

    private async Task EmitTimeoutAsync(
        string key,
        FlowJoinSide side,
        PendingEntry<TLeft> left,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var timeout = new FlowJoinTimeout<TLeft, TRight>
        {
            Key = key,
            Side = side,
            Left = left.Value,
            ReceivedAt = left.ReceivedAt,
            TimedOutAt = now,
            Timeout = _timeout
        };

        await EmitTimeoutCoreAsync(timeout, key, side, cancellationToken).ConfigureAwait(false);
    }

    private async Task EmitTimeoutAsync(
        string key,
        FlowJoinSide side,
        PendingEntry<TRight> right,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var timeout = new FlowJoinTimeout<TLeft, TRight>
        {
            Key = key,
            Side = side,
            Right = right.Value,
            ReceivedAt = right.ReceivedAt,
            TimedOutAt = now,
            Timeout = _timeout
        };

        await EmitTimeoutCoreAsync(timeout, key, side, cancellationToken).ConfigureAwait(false);
    }

    private async Task EmitTimeoutCoreAsync(
        FlowJoinTimeout<TLeft, TRight> timeout,
        string key,
        FlowJoinSide side,
        CancellationToken cancellationToken)
    {
        await _timeouts.SendAsync(timeout, cancellationToken).ConfigureAwait(false);
        TryEmitDiagnostic(
            RoutingDiagnosticNames.JoinTimedOut,
            FlowDiagnosticLevel.Warning,
            "flow.join emitted timeout.",
            attributes: JoinNodeSupport.CreateAttributes(
                _options,
                _expressionEngine,
                _pendingCount,
                key,
                side));
    }

    private async Task CompleteSideAsync(
        FlowJoinSide side,
        Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } completionException)
        {
            ((IDataflowBlock)_commands).Fault(completionException);
            return;
        }

        try
        {
            await _commands.SendAsync(
                JoinCommand.CompleteSide(side),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ((IDataflowBlock)_commands).Fault(exception);
        }
    }

    private async Task CompleteSideCoreAsync(FlowJoinSide side)
    {
        if (side == FlowJoinSide.Left)
        {
            _leftCompleted = true;
        }
        else
        {
            _rightCompleted = true;
        }

        if (!_leftCompleted || !_rightCompleted)
        {
            return;
        }

        TryCancelTimer();
        await EmitExpiredAsync(_clock.UtcNow, force: true, CancellationToken.None)
            .ConfigureAwait(false);
        _commands.Complete();
    }

    private void CompleteOutputs(Task completion)
    {
        TryCancelTimer();
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            ((IDataflowBlock)_timeouts).Fault(exception);
            return;
        }

        _output.Complete();
        _timeouts.Complete();
    }

    private void ScheduleTimer(DateTimeOffset now)
    {
        _timerCancellation?.Cancel();
        _timerCancellation?.Dispose();
        _timerVersion++;
        if (_pendingCount == 0 || _leftCompleted && _rightCompleted)
        {
            _timerCancellation = null;
            return;
        }

        var dueAt = GetNextDueAt();
        if (dueAt is null)
        {
            _timerCancellation = null;
            return;
        }

        var delay = dueAt.Value <= now
            ? TimeSpan.Zero
            : dueAt.Value - now;
        var version = _timerVersion;
        _timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifecycleCancellation.Token);
        _ = RunTimerAsync(version, delay, _timerCancellation.Token);
    }

    private DateTimeOffset? GetNextDueAt()
    {
        while (_deadlines.Count > 0)
        {
            var deadline = _deadlines.Peek();
            if (TryPeekPendingEntry(deadline, out _))
            {
                return deadline.ReceivedAt + _timeout;
            }

            _deadlines.Dequeue();
        }

        return null;
    }

    private void TryCancelTimer()
    {
        var timerCancellation = _timerCancellation;
        if (timerCancellation is null)
        {
            return;
        }

        try
        {
            timerCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ScheduleTimer disposed the source on the processor thread.
        }
    }

    private async Task RunTimerAsync(
        long version,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await _clock.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
            await _commands.SendAsync(
                JoinCommand.Timer(version),
                _lifecycleCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void RemoveIfEmpty(
        string key,
        PendingBucket bucket)
    {
        if (bucket.Lefts.Count == 0 && bucket.Rights.Count == 0)
        {
            _pending.Remove(key);
        }
    }

    private void ReportJoinError(
        int code,
        string message,
        Exception? exception,
        string? key = null,
        FlowJoinSide? side = null)
    {
        TryReportError(
            code,
            message,
            exception,
            JoinNodeSupport.CreateErrorContext(
                _options,
                _expressionEngine,
                key,
                side));
        TryEmitDiagnostic(
            RoutingDiagnosticNames.JoinFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            JoinNodeSupport.CreateAttributes(
                _options,
                _expressionEngine,
                _pendingCount,
                key,
                side));
    }

    private sealed record PendingEntry<TValue>(
        TValue Value,
        DateTimeOffset ReceivedAt);

    private sealed record JoinDeadline(
        string Key,
        FlowJoinSide Side,
        DateTimeOffset ReceivedAt);

    private sealed class PendingBucket
    {
        public Queue<PendingEntry<TLeft>> Lefts { get; } = [];
        public Queue<PendingEntry<TRight>> Rights { get; } = [];
    }

    private sealed record JoinCommand(
        JoinCommandKind Kind,
        TLeft? Left = default,
        TRight? Right = default,
        FlowJoinSide? Side = null,
        long TimerVersion = 0)
    {
        public static JoinCommand FromLeft(TLeft input)
            => new(JoinCommandKind.Left, Left: input);

        public static JoinCommand FromRight(TRight input)
            => new(JoinCommandKind.Right, Right: input);

        public static JoinCommand Timer(long version)
            => new(JoinCommandKind.Timer, TimerVersion: version);

        public static JoinCommand CompleteSide(FlowJoinSide side)
            => new(JoinCommandKind.CompleteSide, Side: side);
    }

    private enum JoinCommandKind
    {
        Left,
        Right,
        Timer,
        CompleteSide
    }

    private sealed class JoinException(
        int code,
        string message,
        Exception? innerException = null,
        string? key = null,
        FlowJoinSide? side = null)
        : Exception(message, innerException)
    {
        public int Code { get; } = code;
        public string? Key { get; } = key;
        public FlowJoinSide? Side { get; } = side;
    }
}
