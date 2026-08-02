using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>
/// Internal join runtime with two inputs, built directly on
/// TPL Dataflow primitives rather than the single-input <see cref="FlowNode{TInput,TOutput}"/>
/// base (the kit has no two-input base by design). Post <c>FlowMessage&lt;TLeft&gt;</c> to
/// <c>Left</c> and <c>FlowMessage&lt;TRight&gt;</c> to <c>Right</c>; the node extracts a key
/// from each payload (via the injected selectors), pairs a left with its matching right by
/// key in FIFO order, and broadcasts a
/// <c>FlowMessage&lt;FlowJoinOutcome&lt;TLeft,TRight&gt;&gt;</c> on <c>Output</c> carrying
/// either a typed match or timeout outcome. Operational errors also travel on
/// <c>Output</c> as <see cref="FlowError"/> messages, while diagnostics use
/// <c>Events</c>. The source ports broadcast, so one output can feed many consumers.
/// </summary>
internal sealed class JoinNodeRuntime<TLeft, TRight> : IFlowNode
{
    private readonly JoinRoutingOptions _options;
    private readonly Func<TLeft, string?> _leftSelector;
    private readonly Func<TRight, string?> _rightSelector;
    private readonly string? _engineName;
    private readonly TimeProvider _clock;
    private readonly StringComparer _comparer;
    private readonly JoinPendingStore<TLeft, TRight> _pending;
    private readonly TimeSpan _timeout;

    private readonly BufferBlock<FlowMessage<TLeft>> _left;
    private readonly BufferBlock<FlowMessage<TRight>> _right;
    private readonly ActionBlock<FlowMessage<TLeft>> _leftFeeder;
    private readonly ActionBlock<FlowMessage<TRight>> _rightFeeder;
    private readonly ActionBlock<JoinCommand> _commands;
    private readonly FlowOutput<FlowMessage<FlowJoinOutcome<TLeft, TRight>>> _output;
    private readonly BroadcastBlock<FlowEvent> _events;

    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private volatile CancellationTokenSource? _timerCancellation;
    private long _timerVersion;
    private bool _leftCompleted;
    private bool _rightCompleted;
    private int _disposed;

    public JoinNodeRuntime(
        JoinRoutingOptions options,
        Func<TLeft, string?> leftSelector,
        Func<TRight, string?> rightSelector,
        string? engineName = null,
        TimeProvider? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _leftSelector = leftSelector ?? throw new ArgumentNullException(nameof(leftSelector));
        _rightSelector = rightSelector ?? throw new ArgumentNullException(nameof(rightSelector));
        _engineName = engineName;
        _clock = clock ?? TimeProvider.System;
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

        _comparer = options.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        _pending = new JoinPendingStore<TLeft, TRight>(_comparer, options.MaxPending);
        _timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds);

        _output = new FlowOutput<FlowMessage<FlowJoinOutcome<TLeft, TRight>>>(
            new FlowOutputOptions { Capacity = options.BoundedCapacity });
        _events = new BroadcastBlock<FlowEvent>(static value => value);

        var executionOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        var blockOptions = new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity };

        _commands = new ActionBlock<JoinCommand>(ProcessCommandAsync, executionOptions);

        // Left/Right are bounded BufferBlocks (the public input surface); each links to a
        // serial feeder that forwards into the single ordered command pump. Completion is
        // watched on the *feeder* — its Completion fires only after every queued data
        // command has been sent to _commands, so the CompleteSide signal can never overtake
        // an in-flight data message and lose a pairing.
        _left = new BufferBlock<FlowMessage<TLeft>>(blockOptions);
        _right = new BufferBlock<FlowMessage<TRight>>(blockOptions);
        _leftFeeder = new ActionBlock<FlowMessage<TLeft>>(
            async message => await _commands.SendAsync(
                JoinCommand.FromLeft(message), _lifecycleCancellation.Token).ConfigureAwait(false),
            executionOptions);
        _rightFeeder = new ActionBlock<FlowMessage<TRight>>(
            async message => await _commands.SendAsync(
                JoinCommand.FromRight(message), _lifecycleCancellation.Token).ConfigureAwait(false),
            executionOptions);
        _left.LinkTo(_leftFeeder, new DataflowLinkOptions { PropagateCompletion = true });
        _right.LinkTo(_rightFeeder, new DataflowLinkOptions { PropagateCompletion = true });

        _leftFeeder.Completion.ContinueWith(
            completion => _ = CompleteSideAsync(FlowJoinSide.Left, completion),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _rightFeeder.Completion.ContinueWith(
            completion => _ = CompleteSideAsync(FlowJoinSide.Right, completion),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _commands.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _ = ObserveOutputFailureAsync();
    }

    /// <summary>Left input port — a bounded buffer; <c>SendAsync</c> applies backpressure.</summary>
    public ITargetBlock<FlowMessage<TLeft>> Left => _left;

    /// <summary>Right input port — a bounded buffer; <c>SendAsync</c> applies backpressure.</summary>
    public ITargetBlock<FlowMessage<TRight>> Right => _right;

    /// <summary>Matched pairs, carrying the left message's correlation id (primary output).</summary>
    public ISourceBlock<FlowMessage<FlowJoinOutcome<TLeft, TRight>>> Output => _output;

    /// <summary>Event port — broadcast.</summary>
    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public void Complete()
    {
        _left.Complete();
        _right.Complete();
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _lifecycleCancellation.Cancel();
        TryCancelTimer();
        ((IDataflowBlock)_left).Fault(exception);
        ((IDataflowBlock)_right).Fault(exception);
        ((IDataflowBlock)_leftFeeder).Fault(exception);
        ((IDataflowBlock)_rightFeeder).Fault(exception);
        ((IDataflowBlock)_commands).Fault(exception);

        // The data output is authoritative; diagnostics are completed so accepted events flush.
        _output.Fault(exception);
        _events.Complete();

        _completion.TrySetException(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

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
            await _output.DisposeAsync().ConfigureAwait(false);
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
            await ReportCommandErrorAsync(command, exception.Code, exception.Message,
                exception.InnerException, exception.Key, exception.Side).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await ReportCommandErrorAsync(command, RoutingErrorCodes.JoinFailed,
                $"flow.join failed: {exception.Message}", exception).ConfigureAwait(false);
        }
    }

    private async Task AddLeftAsync(FlowMessage<TLeft> message)
    {
        if (message.IsError)
        {
            await SendOutputAsync(
                message.WithError<FlowJoinOutcome<TLeft, TRight>>(message.Error!),
                _lifecycleCancellation.Token).ConfigureAwait(false);
            return;
        }

        var now = _clock.GetUtcNow();
        await EmitExpiredAsync(now, force: false, _lifecycleCancellation.Token).ConfigureAwait(false);
        var key = EvaluateLeftKey(message.Value);
        await AddLeftCoreAsync(key, message, now).ConfigureAwait(false);
        ScheduleTimer(_clock.GetUtcNow());
    }

    private async Task AddRightAsync(FlowMessage<TRight> message)
    {
        if (message.IsError)
        {
            await SendOutputAsync(
                message.WithError<FlowJoinOutcome<TLeft, TRight>>(message.Error!),
                _lifecycleCancellation.Token).ConfigureAwait(false);
            return;
        }

        var now = _clock.GetUtcNow();
        await EmitExpiredAsync(now, force: false, _lifecycleCancellation.Token).ConfigureAwait(false);
        var key = EvaluateRightKey(message.Value);
        await AddRightCoreAsync(key, message, now).ConfigureAwait(false);
        ScheduleTimer(_clock.GetUtcNow());
    }

    private string EvaluateLeftKey(TLeft input)
    {
        string? key;
        try
        {
            key = _leftSelector(input);
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
            key = _rightSelector(input);
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

    private static string ValidateKey(string? key, FlowJoinSide side)
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

    private async Task AddLeftCoreAsync(string key, FlowMessage<TLeft> message, DateTimeOffset now)
    {
        if (_pending.TryTakeRight(key, out var right))
        {
            await EmitResultAsync(key, new JoinPendingEntry<TLeft>(message, now), right, now)
                .ConfigureAwait(false);
            return;
        }

        if (!await CanTrackPendingAsync(key, FlowJoinSide.Left, message).ConfigureAwait(false))
        {
            return;
        }

        _pending.AddLeft(key, message, now);
    }

    private async Task AddRightCoreAsync(string key, FlowMessage<TRight> message, DateTimeOffset now)
    {
        if (_pending.TryTakeLeft(key, out var left))
        {
            await EmitResultAsync(key, left, new JoinPendingEntry<TRight>(message, now), now)
                .ConfigureAwait(false);
            return;
        }

        if (!await CanTrackPendingAsync(key, FlowJoinSide.Right, message).ConfigureAwait(false))
        {
            return;
        }

        _pending.AddRight(key, message, now);
    }

    private async Task<bool> CanTrackPendingAsync<TSource>(
        string key,
        FlowJoinSide side,
        FlowMessage<TSource> source)
    {
        if (_pending.CanTrack)
        {
            return true;
        }

        await ReportJoinErrorAsync(
            RoutingErrorCodes.JoinCapacityExceeded,
            $"flow.join maxPending limit reached; key '{key}' was not tracked.",
            null,
            key,
            side,
            source).ConfigureAwait(false);
        return false;
    }

    private async Task ExpireByTimerAsync(long version)
    {
        if (version != _timerVersion)
        {
            return;
        }

        await EmitExpiredAsync(_clock.GetUtcNow(), force: false, _lifecycleCancellation.Token)
            .ConfigureAwait(false);
        ScheduleTimer(_clock.GetUtcNow());
    }

    private async Task EmitExpiredAsync(DateTimeOffset now, bool force, CancellationToken cancellationToken)
    {
        foreach (var expired in _pending.TakeExpired(now, _timeout, force))
        {
            if (expired.Left is { } left)
            {
                await EmitTimeoutAsync(expired.Key, expired.Side, left, now, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (expired.Right is { } right)
            {
                await EmitTimeoutAsync(expired.Key, expired.Side, right, now, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task EmitResultAsync(
        string key,
        JoinPendingEntry<TLeft> left,
        JoinPendingEntry<TRight> right,
        DateTimeOffset now)
    {
        var result = new FlowJoinResult<TLeft, TRight>
        {
            Key = key,
            Left = left.Message.Value,
            Right = right.Message.Value,
            LeftReceivedAt = left.ReceivedAt,
            RightReceivedAt = right.ReceivedAt,
            JoinedAt = now,
            Elapsed = now - (left.ReceivedAt <= right.ReceivedAt
                ? left.ReceivedAt
                : right.ReceivedAt)
        };

        // The matched pair carries the left message's correlation id forward.
        await SendOutputAsync(
                left.Message.With<FlowJoinOutcome<TLeft, TRight>>(
                    new FlowJoinMatchedOutcome<TLeft, TRight> { Match = result }),
                _lifecycleCancellation.Token)
            .ConfigureAwait(false);
        _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = left.Message.CorrelationId,
            Name = RoutingDiagnosticNames.JoinMatched,
            Level = FlowEventLevel.Information,
            Message = "flow.join matched values.",
            Attributes = CreateAttributes(key)
        });
    }

    private async Task EmitTimeoutAsync(
        string key,
        FlowJoinSide side,
        JoinPendingEntry<TLeft> left,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var timeout = new FlowJoinTimeout<TLeft, TRight>
        {
            Key = key,
            Side = side,
            Left = left.Message.Value,
            ReceivedAt = left.ReceivedAt,
            TimedOutAt = now,
            Timeout = _timeout
        };

        await EmitTimeoutCoreAsync(
                left.Message.With<FlowJoinOutcome<TLeft, TRight>>(
                    new FlowJoinTimedOutOutcome<TLeft, TRight> { Timeout = timeout }),
                left.Message.CorrelationId,
                key,
                side,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EmitTimeoutAsync(
        string key,
        FlowJoinSide side,
        JoinPendingEntry<TRight> right,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var timeout = new FlowJoinTimeout<TLeft, TRight>
        {
            Key = key,
            Side = side,
            Right = right.Message.Value,
            ReceivedAt = right.ReceivedAt,
            TimedOutAt = now,
            Timeout = _timeout
        };

        await EmitTimeoutCoreAsync(
                right.Message.With<FlowJoinOutcome<TLeft, TRight>>(
                    new FlowJoinTimedOutOutcome<TLeft, TRight> { Timeout = timeout }),
                right.Message.CorrelationId,
                key,
                side,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EmitTimeoutCoreAsync(
        FlowMessage<FlowJoinOutcome<TLeft, TRight>> message,
        CorrelationId? correlationId,
        string key,
        FlowJoinSide side,
        CancellationToken cancellationToken)
    {
        await SendOutputAsync(message, cancellationToken).ConfigureAwait(false);
        _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = correlationId,
            Name = RoutingDiagnosticNames.JoinTimedOut,
            Level = FlowEventLevel.Warning,
            Message = "flow.join emitted timeout.",
            Attributes = CreateAttributes(key, side)
        });
    }

    private async Task CompleteSideAsync(FlowJoinSide side, Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } completionException)
        {
            ((IDataflowBlock)_commands).Fault(completionException);
            return;
        }

        try
        {
            await _commands.SendAsync(JoinCommand.CompleteSide(side), CancellationToken.None)
                .ConfigureAwait(false);
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
        await EmitExpiredAsync(_clock.GetUtcNow(), force: true, CancellationToken.None)
            .ConfigureAwait(false);
        _commands.Complete();
    }

    private void CompleteOutputs(Task completion)
    {
        TryCancelTimer();
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            _output.Fault(exception);
            // Flush diagnostics rather than discard accepted events.
            _events.Complete();
            _completion.TrySetException(exception);
            return;
        }

        _output.Complete();
        _events.Complete();
        Task.WhenAll(
                _output.Completion,
                _events.Completion)
            .ContinueWith(
                outputs =>
                {
                    if (outputs.IsFaulted && outputs.Exception is { } outputsException)
                    {
                        _completion.TrySetException(outputsException);
                    }
                    else
                    {
                        _completion.TrySetResult();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private void ScheduleTimer(DateTimeOffset now)
    {
        var previous = Interlocked.Exchange(ref _timerCancellation, null);
        previous?.Cancel();
        previous?.Dispose();
        _timerVersion++;
        if (_pending.Count == 0 || _leftCompleted && _rightCompleted)
        {
            return;
        }

        var dueAt = _pending.GetNextDueAt(_timeout);
        if (dueAt is null)
        {
            return;
        }

        var delay = dueAt.Value <= now ? TimeSpan.Zero : dueAt.Value - now;
        var version = _timerVersion;
        var timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifecycleCancellation.Token);
        _timerCancellation = timerCancellation;
        _ = RunTimerAsync(version, delay, timerCancellation.Token);
    }

    private void TryCancelTimer()
    {
        var timerCancellation = Interlocked.Exchange(ref _timerCancellation, null);
        timerCancellation?.Cancel();
        timerCancellation?.Dispose();
    }

    private async Task RunTimerAsync(long version, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);
            }

            await _commands.SendAsync(JoinCommand.Timer(version), _lifecycleCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ReportCommandErrorAsync(
        JoinCommand command,
        int code,
        string message,
        Exception? exception,
        string? key = null,
        FlowJoinSide? side = null)
    {
        if (command.Left is { } left)
        {
            await ReportJoinErrorAsync(code, message, exception, key, side, left)
                .ConfigureAwait(false);
            return;
        }

        if (command.Right is { } right)
        {
            await ReportJoinErrorAsync(code, message, exception, key, side, right)
                .ConfigureAwait(false);
            return;
        }

        var error = CreateFlowError(code, message, exception, key, side);
        await SendOutputAsync(
            FlowMessage.CreateError<FlowJoinOutcome<TLeft, TRight>>(error),
            _lifecycleCancellation.Token).ConfigureAwait(false);
        EmitFailureEvent(message, key, side, correlationId: null);
    }

    private async Task ReportJoinErrorAsync<TSource>(
        int code,
        string message,
        Exception? exception,
        string? key,
        FlowJoinSide? side,
        FlowMessage<TSource> source)
    {
        var error = CreateFlowError(code, message, exception, key, side);
        await SendOutputAsync(
            source.WithError<FlowJoinOutcome<TLeft, TRight>>(error),
            _lifecycleCancellation.Token).ConfigureAwait(false);
        EmitFailureEvent(message, key, side, source.CorrelationId);
    }

    private async Task SendOutputAsync(
        FlowMessage<FlowJoinOutcome<TLeft, TRight>> message,
        CancellationToken cancellationToken)
    {
        if (!await _output.SendAsync(message, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("flow.join output declined an accepted result.");
    }

    private async Task ObserveOutputFailureAsync()
    {
        try
        {
            await _output.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Fault(exception);
        }
    }

    private FlowError CreateFlowError(
        int code,
        string message,
        Exception? exception,
        string? key,
        FlowJoinSide? side)
    {
        var details = JsonSerializer.SerializeToElement(new
        {
            legacyCode = code,
            context = CreateErrorContext(key, side),
            exceptionType = exception?.GetType().FullName
        });

        return new FlowError(
            RoutingErrorCodeNames.OperationFailed,
            message,
            "routing",
            exception is TimeoutException,
            details);
    }

    private void EmitFailureEvent(
        string message,
        string? key,
        FlowJoinSide? side,
        CorrelationId? correlationId)
    {
        _events.Post(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = correlationId,
            Name = RoutingDiagnosticNames.JoinFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateAttributes(key, side)
        });
    }

    private Dictionary<string, object?> CreateAttributes(
        string? key = null,
        FlowJoinSide? side = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["leftInputType"] = _options.LeftInputType,
            ["rightInputType"] = _options.RightInputType,
            ["engine"] = _engineName,
            ["caseSensitive"] = _options.CaseSensitive,
            ["timeoutMilliseconds"] = _options.TimeoutMilliseconds,
            ["maxPending"] = _options.MaxPending,
            ["pendingCount"] = _pending.Count
        };

        if (!string.IsNullOrWhiteSpace(key))
        {
            attributes["key"] = key;
        }

        if (side.HasValue)
        {
            attributes["side"] = side.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
        {
            attributes["expressionId"] = _options.ExpressionId;
        }

        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
        {
            attributes["expressionName"] = _options.ExpressionName;
        }

        return attributes;
    }

    private string CreateErrorContext(string? key = null, FlowJoinSide? side = null)
    {
        var values = new List<string>
        {
            $"leftInputType={_options.LeftInputType}",
            $"rightInputType={_options.RightInputType}",
            $"engine={_engineName}",
            $"timeoutMilliseconds={_options.TimeoutMilliseconds}",
            $"maxPending={_options.MaxPending}"
        };

        if (!string.IsNullOrWhiteSpace(key))
        {
            values.Add($"key={key}");
        }

        if (side.HasValue)
        {
            values.Add($"side={side.Value}");
        }

        if (!string.IsNullOrWhiteSpace(_options.ExpressionId))
        {
            values.Add($"expressionId={_options.ExpressionId}");
        }

        if (!string.IsNullOrWhiteSpace(_options.ExpressionName))
        {
            values.Add($"expressionName={_options.ExpressionName}");
        }

        return string.Join("; ", values);
    }

    private sealed record JoinCommand(
        JoinCommandKind Kind,
        FlowMessage<TLeft>? Left = null,
        FlowMessage<TRight>? Right = null,
        FlowJoinSide? Side = null,
        long TimerVersion = 0)
    {
        public static JoinCommand FromLeft(FlowMessage<TLeft> message)
            => new(JoinCommandKind.Left, Left: message);

        public static JoinCommand FromRight(FlowMessage<TRight> message)
            => new(JoinCommandKind.Right, Right: message);

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
