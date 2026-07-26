using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using System.Text.Json;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>
/// Internal windowing runtime. Post <c>FlowMessage&lt;TInput&gt;</c> values to <c>Input</c>;
/// the node groups their payloads into count- or time-bounded windows and broadcasts each
/// completed window as a <c>FlowMessage&lt;FlowWindow&lt;TInput&gt;&gt;</c> on <c>Output</c>.
/// A window emits when <see cref="WindowRoutingOptions.MaxItems"/> items are buffered, when
/// the configured time elapses with no further input (timed off the injected
/// <see cref="TimeProvider"/>), or — by default — as a partial window when the input drains.
/// The window carries the correlation id of the message that opened it.
/// </summary>
internal sealed class WindowNodeRuntime<TInput> : FlowNode<TInput, FlowWindow<TInput>>
{
    private readonly WindowRoutingOptions _options;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _timeLimit;
    private readonly object _gate = new();
    private readonly List<TInput> _items = [];
    private FlowMessage<TInput>? _windowSource;
    private DateTimeOffset? _startedAt;
    private long _nextSequence;
    private long _windowVersion;
    private ITimer? _timer;

    public WindowNodeRuntime(
        WindowRoutingOptions options,
        TimeProvider? clock = null)
        : this(ValidateOptions(options), clock)
    {
    }

    private WindowNodeRuntime(ValidatedOptions options, TimeProvider? clock)
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
            FlowMessage<TInput>? source = null;
            lock (_gate)
            {
                if (_items.Count == 0)
                {
                    StartWindow(message, _clock.GetUtcNow());
                }

                _items.Add(message.Value);
                if (_options.MaxItems > 0 && _items.Count >= _options.MaxItems)
                {
                    source = _windowSource;
                    window = BuildAndClearWindow(FlowWindowEmitReason.Count, _clock.GetUtcNow());
                }
            }

            if (window is not null && source is not null)
            {
                EmitWindow(window, source);
            }
        }
        catch (Exception exception)
        {
            ReportFailure(message, exception);
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
        FlowMessage<TInput>? source = null;
        lock (_gate)
        {
            CancelTimer();
            if (_items.Count > 0 && _options.EmitPartialOnCompletion)
            {
                source = _windowSource;
                window = BuildAndClearWindow(FlowWindowEmitReason.Completion, _clock.GetUtcNow());
            }
            else
            {
                ClearWindow();
            }
        }

        if (window is not null && source is not null)
        {
            EmitWindow(window, source);
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
        lock (_gate)
        {
            if (version == _windowVersion && _items.Count > 0)
            {
                var source = _windowSource;
                var window = BuildAndClearWindow(FlowWindowEmitReason.Time, _clock.GetUtcNow());
                if (window is not null && source is not null)
                    EmitWindow(window, source);
            }
        }
    }

    private void StartWindow(FlowMessage<TInput> source, DateTimeOffset startedAt)
    {
        _startedAt = startedAt;
        _windowSource = source;
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
        _windowSource = null;
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

    private void EmitWindow(FlowWindow<TInput> window, FlowMessage<TInput> source)
    {
        Emit(source.With(window));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = RoutingDiagnosticNames.WindowEmitted,
            Level = FlowEventLevel.Information,
            Message = "flow.window emitted window.",
            Attributes = CreateAttributes(window)
        });
    }

    private void ReportFailure(FlowMessage<TInput> source, Exception exception)
    {
        var details = JsonSerializer.SerializeToElement(new
        {
            legacyCode = RoutingErrorCodes.WindowFailed,
            context = CreateErrorContext(),
            exceptionType = exception.GetType().FullName
        });
        Emit(source.WithError<FlowWindow<TInput>>(new FlowError(
            RoutingErrorCodeNames.OperationFailed,
            $"flow.window failed: {exception.Message}",
            "routing",
            exception is TimeoutException,
            details)));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
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
