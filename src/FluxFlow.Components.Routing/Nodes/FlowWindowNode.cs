using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>
/// A standalone windowing node. Post <c>FlowMessage&lt;TInput&gt;</c> values to <c>Input</c>;
/// the node groups their payloads into count- or time-bounded windows and broadcasts each
/// completed window as a <c>FlowMessage&lt;FlowWindow&lt;TInput&gt;&gt;</c> on <c>Output</c>.
/// A window emits when <see cref="WindowRoutingOptions.MaxItems"/> items are buffered, when
/// the configured time elapses with no further input (timed off the injected
/// <see cref="TimeProvider"/>), or — by default — as a partial window when the input drains.
/// The window carries the correlation id of the message that opened it. Works with nothing
/// but <c>new FlowWindowNode&lt;T&gt;(options)</c> — no engine.
/// </summary>
public sealed class FlowWindowNode<TInput> : FlowNode<TInput, FlowWindow<TInput>>
{
    private readonly WindowRoutingOptions _options;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _timeLimit;
    private readonly object _gate = new();
    private readonly List<TInput> _items = [];
    private CorrelationId _windowCorrelationId;
    private DateTimeOffset? _startedAt;
    private long _nextSequence;
    private long _windowVersion;
    private ITimer? _timer;

    public FlowWindowNode(
        WindowRoutingOptions options,
        TimeProvider? clock = null)
        : this(ValidateOptions(options), clock)
    {
    }

    private FlowWindowNode(ValidatedOptions options, TimeProvider? clock)
        : base(options.FlowNodeOptions)
    {
        _options = options.WindowOptions;
        _clock = clock ?? TimeProvider.System;
        _timeLimit = TimeSpan.FromMilliseconds(options.WindowOptions.TimeMilliseconds);
    }

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            FlowWindow<TInput>? window = null;
            CorrelationId correlation = default;
            lock (_gate)
            {
                if (_items.Count == 0)
                {
                    StartWindow(message.CorrelationId, _clock.GetUtcNow());
                }

                _items.Add(message.Payload);
                if (_options.MaxItems > 0 && _items.Count >= _options.MaxItems)
                {
                    correlation = _windowCorrelationId;
                    window = BuildAndClearWindow(FlowWindowEmitReason.Count, _clock.GetUtcNow());
                }
            }

            if (window is not null)
            {
                EmitWindow(window, correlation);
            }
        }
        catch (Exception exception)
        {
            ReportFailure(message.CorrelationId, exception);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Flushes a partial window when the input drains (unless suppressed). Runs after the
    /// pump stops and before the outputs complete, so the emitted window reaches consumers.
    /// </summary>
    protected override ValueTask OnInputCompletedAsync()
    {
        FlowWindow<TInput>? window = null;
        CorrelationId correlation = default;
        lock (_gate)
        {
            CancelTimer();
            if (_items.Count > 0 && _options.EmitPartialOnCompletion)
            {
                correlation = _windowCorrelationId;
                window = BuildAndClearWindow(FlowWindowEmitReason.Completion, _clock.GetUtcNow());
            }
            else
            {
                ClearWindow();
            }
        }

        if (window is not null)
        {
            EmitWindow(window, correlation);
        }

        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposeAsync()
    {
        lock (_gate)
        {
            CancelTimer();
        }

        return ValueTask.CompletedTask;
    }

    // Fired once the time window elapses for the window identified by <paramref name="state"/>.
    private void OnTimeElapsed(object? state)
    {
        var version = (long)state!;
        FlowWindow<TInput>? window = null;
        CorrelationId correlation = default;
        lock (_gate)
        {
            if (version == _windowVersion && _items.Count > 0)
            {
                correlation = _windowCorrelationId;
                window = BuildAndClearWindow(FlowWindowEmitReason.Time, _clock.GetUtcNow());
            }
        }

        if (window is not null)
        {
            EmitWindow(window, correlation);
        }
    }

    private void StartWindow(CorrelationId correlationId, DateTimeOffset startedAt)
    {
        _startedAt = startedAt;
        _windowCorrelationId = correlationId;
        _windowVersion++;
        ScheduleTimer(_windowVersion);
    }

    // Builds the window snapshot and resets state. Must be called under _gate.
    private FlowWindow<TInput>? BuildAndClearWindow(
        FlowWindowEmitReason reason,
        DateTimeOffset emittedAt)
    {
        if (_items.Count == 0 || _startedAt is null)
        {
            return null;
        }

        CancelTimer();
        var window = new FlowWindow<TInput>
        {
            Sequence = ++_nextSequence,
            Items = _items.ToArray(),
            StartedAt = _startedAt.Value,
            EmittedAt = emittedAt,
            Reason = reason
        };
        ClearWindow();
        return window;
    }

    // Must be called under _gate.
    private void ClearWindow()
    {
        _items.Clear();
        _startedAt = null;
        _windowVersion++;
    }

    // Must be called under _gate.
    private void ScheduleTimer(long version)
    {
        if (_timeLimit <= TimeSpan.Zero)
        {
            return;
        }

        _timer?.Dispose();
        _timer = _clock.CreateTimer(OnTimeElapsed, version, _timeLimit, Timeout.InfiniteTimeSpan);
    }

    // Must be called under _gate.
    private void CancelTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void EmitWindow(FlowWindow<TInput> window, CorrelationId correlationId)
    {
        Emit(new FlowMessage<FlowWindow<TInput>>(correlationId, window));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = correlationId,
            Name = RoutingDiagnosticNames.WindowEmitted,
            Level = FlowEventLevel.Information,
            Message = "flow.window emitted window.",
            Attributes = CreateAttributes(window)
        });
    }

    private void ReportFailure(CorrelationId correlationId, Exception exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = correlationId,
            Code = RoutingErrorCodes.WindowFailed,
            Message = $"flow.window failed: {exception.Message}",
            Context = CreateErrorContext(),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = correlationId,
            Name = RoutingDiagnosticNames.WindowFailed,
            Level = FlowEventLevel.Error,
            Message = "flow.window failed.",
            Attributes = CreateAttributes()
        });
    }

    private Dictionary<string, object?> CreateAttributes(FlowWindow<TInput>? window = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = _options.InputType,
            ["maxItems"] = _options.MaxItems,
            ["timeMilliseconds"] = _options.TimeMilliseconds,
            ["emitPartialOnCompletion"] = _options.EmitPartialOnCompletion
        };

        if (window is not null)
        {
            attributes["sequence"] = window.Sequence;
            attributes["count"] = window.Count;
            attributes["reason"] = window.Reason.ToString();
            attributes["durationMilliseconds"] = window.Duration.TotalMilliseconds;
        }

        return attributes;
    }

    private string CreateErrorContext()
        => $"inputType={_options.InputType}; maxItems={_options.MaxItems}; timeMilliseconds={_options.TimeMilliseconds}";

    private static ValidatedOptions ValidateOptions(WindowRoutingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new ArgumentException(
                "flow.window option 'inputType' cannot be empty.", nameof(options));
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.window option 'boundedCapacity' must be greater than zero.");
        }

        if (options.MaxItems < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.window option 'maxItems' cannot be negative.");
        }

        if (options.TimeMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.window option 'timeMilliseconds' cannot be negative.");
        }

        if (options.MaxItems == 0 && options.TimeMilliseconds == 0)
        {
            throw new ArgumentException(
                "flow.window requires maxItems or timeMilliseconds.",
                nameof(options));
        }

        return new ValidatedOptions(options);
    }

    private sealed class ValidatedOptions(WindowRoutingOptions windowOptions)
    {
        public WindowRoutingOptions WindowOptions { get; } = windowOptions;

        public FlowNodeOptions FlowNodeOptions { get; } = new()
        {
            InputCapacity = windowOptions.BoundedCapacity
        };
    }
}
