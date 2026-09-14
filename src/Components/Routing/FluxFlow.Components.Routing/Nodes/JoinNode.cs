using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>
/// Joins two canonical input streams by key in FIFO order using bounded native blocks.
/// Typed selector values remain local to processing and pending state. Matches, timeouts
/// and errors use the same canonical Output; Events are best-effort observations.
/// </summary>
public class JoinNode<TLeft, TRight> : IFlowComponent
{
    private static readonly JsonSerializerOptions OutputJsonOptions = CreateOutputJsonOptions();

    private static JsonSerializerOptions CreateOutputJsonOptions()
    {
        var resolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static type =>
        {
            foreach (var property in type.Properties)
                if (property.PropertyType == typeof(JsonElement))
                    property.ShouldSerialize = static (_, value) => value is JsonElement json && json.ValueKind != JsonValueKind.Undefined;
        });
        return new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
    }

    private readonly JoinRoutingOptions _options;
    private readonly Func<TLeft, string?> _leftSelector;
    private readonly Func<TRight, string?> _rightSelector;
    private readonly string? _engineName;
    private readonly TimeProvider _clock;
    private readonly StringComparer _comparer;
    private readonly JoinPendingStore<TLeft, TRight> _pending;
    private readonly TimeSpan _timeout;

    private readonly ActionBlock<FlowMessage> _left;
    private readonly ActionBlock<FlowMessage> _right;
    private readonly ActionBlock<JoinCommand> _commands;
    private readonly FlowOutput<FlowMessage> _output;
    private readonly BroadcastBlock<FlowMessage> _events;

    private readonly Task _completion;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly CancellationToken _stopping;
    private Exception? _failure;
    private volatile CancellationTokenSource? _timerCancellation;
    private long _timerVersion;
    private bool _leftCompleted;
    private bool _rightCompleted;
    private int _disposed;

    public JoinNode(
        JoinRoutingOptions options,
        Func<TLeft, string?> leftSelector,
        Func<TRight, string?> rightSelector,
        string? engineName = null,
        TimeProvider? clock = null)
    {
        _stopping = _lifecycleCancellation.Token;
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

        _output = new FlowOutput<FlowMessage>(
            new FlowOutputOptions { Capacity = options.BoundedCapacity });
        _events = new BroadcastBlock<FlowMessage>(static value => value);

        var executionOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _commands = new ActionBlock<JoinCommand>(ProcessCommandAsync, executionOptions);

        // A side closes only after its input block has forwarded every accepted item.
        _left = new ActionBlock<FlowMessage>(
            message => ForwardAsync(new JoinCommand(() => AddLeftAsync(message), message)), executionOptions);
        _right = new ActionBlock<FlowMessage>(
            message => ForwardAsync(new JoinCommand(() => AddRightAsync(message), message)), executionOptions);
        Ports = new FlowPorts(
            inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Left"] = Left, ["Right"] = Right },
            outputs: new Dictionary<string, ISourceBlock<FlowMessage>> { ["Output"] = Output },
            events: Events);
        _ = CompleteSideAsync(FlowJoinSide.Left, _left.Completion);
        _ = CompleteSideAsync(FlowJoinSide.Right, _right.Completion);
        _completion = CompleteOutputsAsync();
        _ = ObserveOutputFailureAsync();
    }

    /// <summary>Bounded canonical left input; SendAsync applies backpressure.</summary>
    public ITargetBlock<FlowMessage> Left => _left;

    /// <summary>Bounded canonical right input; SendAsync applies backpressure.</summary>
    public ITargetBlock<FlowMessage> Right => _right;

    /// <summary>Matched pairs, carrying the left message's correlation id (primary output).</summary>
    public ISourceBlock<FlowMessage> Output => _output;

    /// <summary>Event port — broadcast.</summary>
    public ISourceBlock<FlowMessage> Events => _events;

    public FlowPorts Ports { get; }

    public Task Completion => _completion;

    public void Complete()
    {
        _left.Complete();
        _right.Complete();
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.CompareExchange(ref _failure, exception, null) is not null) return;
        FlowDiagnostics.LifecycleException(this, "join", exception);
        _lifecycleCancellation.Cancel();
        TryCancelTimer();
        ((IDataflowBlock)_left).Fault(exception);
        ((IDataflowBlock)_right).Fault(exception);
        ((IDataflowBlock)_commands).Fault(exception);

        // The data output is authoritative; diagnostics are completed so accepted events flush.
        _output.Fault(exception);
        _events.Complete();

        // Completion waits for active input/command work before disposal releases resources.
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
        using var activity = command.Source is { } source
            ? FlowTracing.StartProcessing(source, this)
            : FlowTracing.SuppressContext();
        try
        {
            await command.Execute().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
            activity.Canceled();
            throw;
        }
        catch (JoinException exception)
        {
            activity.Failed(exception);
            if (command.Source is { } input)
                FlowDiagnostics.ProcessingException(input, this, exception);
            else FlowDiagnostics.LifecycleException(this, "join.command", exception);
            await ReportCommandErrorAsync(command, exception.Code, exception.Message,
                exception.InnerException, exception.Key, exception.Side).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            activity.Failed(exception);
            if (command.Source is { } input)
                FlowDiagnostics.ProcessingException(input, this, exception);
            else FlowDiagnostics.LifecycleException(this, "join.command", exception);
            await ReportCommandErrorAsync(command, RoutingErrorCodes.JoinFailed,
                $"flow.join failed: {exception.Message}", exception).ConfigureAwait(false);
        }
    }

    private async Task AddLeftAsync(FlowMessage message)
    {
        if (message.IsError)
        {
            await SendOutputAsync(
                message,
                _stopping).ConfigureAwait(false);
            return;
        }

        var now = _clock.GetUtcNow();
        await EmitExpiredAsync(now, force: false, _stopping).ConfigureAwait(false);
        var value = (message.Value is TLeft typedInput0 ? typedInput0 : typeof(TLeft) == typeof(JsonElement) ? (TLeft)(object)message.Value.Json : message.Value.Json.Deserialize<TLeft>(OutputJsonOptions))!;
        var key = EvaluateLeftKey(value);
        await AddLeftCoreAsync(key, message, value, now).ConfigureAwait(false);
        ScheduleTimer(_clock.GetUtcNow());
    }

    private async Task AddRightAsync(FlowMessage message)
    {
        if (message.IsError)
        {
            await SendOutputAsync(
                message,
                _stopping).ConfigureAwait(false);
            return;
        }

        var now = _clock.GetUtcNow();
        await EmitExpiredAsync(now, force: false, _stopping).ConfigureAwait(false);
        var value = (message.Value is TRight typedInput1 ? typedInput1 : typeof(TRight) == typeof(JsonElement) ? (TRight)(object)message.Value.Json : message.Value.Json.Deserialize<TRight>(OutputJsonOptions))!;
        var key = EvaluateRightKey(value);
        await AddRightCoreAsync(key, message, value, now).ConfigureAwait(false);
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

    private async Task AddLeftCoreAsync(string key, FlowMessage message, TLeft value, DateTimeOffset now)
    {
        if (_pending.TryTakeRight(key, out var right))
        {
            await EmitResultAsync(key, new JoinPendingEntry<TLeft>(message, value, now), right, now)
                .ConfigureAwait(false);
            return;
        }

        if (!await CanTrackPendingAsync(key, FlowJoinSide.Left, message).ConfigureAwait(false))
        {
            return;
        }

        _pending.AddLeft(key, message, value, now);
    }

    private async Task AddRightCoreAsync(string key, FlowMessage message, TRight value, DateTimeOffset now)
    {
        if (_pending.TryTakeLeft(key, out var left))
        {
            await EmitResultAsync(key, left, new JoinPendingEntry<TRight>(message, value, now), now)
                .ConfigureAwait(false);
            return;
        }

        if (!await CanTrackPendingAsync(key, FlowJoinSide.Right, message).ConfigureAwait(false))
        {
            return;
        }

        _pending.AddRight(key, message, value, now);
    }

    private async Task<bool> CanTrackPendingAsync(
        string key,
        FlowJoinSide side,
        FlowMessage source)
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

        await EmitExpiredAsync(_clock.GetUtcNow(), force: false, _stopping)
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
        using var activity = FlowTracing.StartProcessing([left.Message, right.Message], this);
        try
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

            // The matched pair carries the left message's correlation id forward.
            await SendOutputAsync(
                    left.Message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new FlowJoinMatchedOutcome<TLeft, TRight> { Match = result }, OutputJsonOptions))),
                    _stopping)
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
            activity.Outcome("matched");
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            activity.Canceled();
            throw;
        }
        catch (Exception error)
        {
            activity.Failed(error);
            throw;
        }
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
            Left = left.Value,
            ReceivedAt = left.ReceivedAt,
            TimedOutAt = now,
            Timeout = _timeout
        };

        await EmitTimeoutCoreAsync(
                left.Message,
                ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new FlowJoinTimedOutOutcome<TLeft, TRight> { Timeout = timeout }, OutputJsonOptions))),
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
            Right = right.Value,
            ReceivedAt = right.ReceivedAt,
            TimedOutAt = now,
            Timeout = _timeout
        };

        await EmitTimeoutCoreAsync(
                right.Message,
                ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new FlowJoinTimedOutOutcome<TLeft, TRight> { Timeout = timeout }, OutputJsonOptions))),
                key,
                side,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EmitTimeoutCoreAsync(
        FlowMessage source,
        FlowValue value,
        string key,
        FlowJoinSide side,
        CancellationToken cancellationToken)
    {
        using var activity = FlowTracing.StartProcessing(source, this);
        try
        {
            await SendOutputAsync(source.With(value), cancellationToken).ConfigureAwait(false);
            _events.Post(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = source.CorrelationId,
                Name = RoutingDiagnosticNames.JoinTimedOut,
                Level = FlowEventLevel.Warning,
                Message = "flow.join emitted timeout.",
                Attributes = CreateAttributes(key, side)
            });
            activity.Outcome("timed_out");
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            activity.Canceled();
            throw;
        }
        catch (Exception error)
        {
            activity.Failed(error);
            throw;
        }
    }

    private async Task CompleteSideAsync(FlowJoinSide side, Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
            await ForwardAsync(new JoinCommand(() => CompleteSideCoreAsync(side))).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Fault(exception);
        }
    }

    private async Task ForwardAsync(JoinCommand command)
    {
        if (!await _commands.SendAsync(command, _stopping).ConfigureAwait(false))
            throw new InvalidOperationException("flow.join is no longer accepting input.");
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
        await EmitExpiredAsync(_clock.GetUtcNow(), force: true, _stopping)
            .ConfigureAwait(false);
        _commands.Complete();
    }

    private async Task CompleteOutputsAsync()
    {
        try
        {
            await Task.WhenAll(_left.Completion, _right.Completion, _commands.Completion).ConfigureAwait(false);
            _output.Complete();
        }
        catch (Exception error) { Fault(error); }
        finally
        {
            TryCancelTimer();
            _events.Complete();
        }
        try { await Task.WhenAll(_output.Completion, _events.Completion).ConfigureAwait(false); }
        catch (Exception error) { Fault(error); }
        if (_failure is { } failure)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
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
            _stopping);
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

            await _commands.SendAsync(new JoinCommand(() => ExpireByTimerAsync(version)), _stopping)
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
        if (command.Source is { } source)
        {
            await ReportJoinErrorAsync(code, message, exception, key, side, source)
                .ConfigureAwait(false);
            return;
        }

        var error = CreateFlowError(code, message, exception, key, side);
        await SendOutputAsync(
            FlowMessage.CreateError(error),
            _stopping).ConfigureAwait(false);
        EmitFailureEvent(message, key, side, correlationId: null);
    }

    private async Task ReportJoinErrorAsync(
        int code,
        string message,
        Exception? exception,
        string? key,
        FlowJoinSide? side,
        FlowMessage source)
    {
        var error = CreateFlowError(code, message, exception, key, side);
        await SendOutputAsync(
            source.WithError(error),
            _stopping).ConfigureAwait(false);
        EmitFailureEvent(message, key, side, source.CorrelationId);
    }

    private async Task SendOutputAsync(
        FlowMessage message,
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

    private sealed record JoinCommand(Func<Task> Execute, FlowMessage? Source = null);

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

/// <summary>Join component with JSON-local selectors and canonical ports.</summary>
public sealed class JsonJoinNode(
    JoinRoutingOptions options,
    Func<JsonElement, string?> leftKeySelector,
    Func<JsonElement, string?> rightKeySelector,
    string? engineName = null,
    TimeProvider? clock = null)
    : JoinNode<JsonElement, JsonElement>(options, leftKeySelector, rightKeySelector, engineName, clock);
