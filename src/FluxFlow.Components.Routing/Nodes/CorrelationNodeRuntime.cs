using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>
/// Internal correlation runtime. Post <c>FlowMessage&lt;TInput&gt;</c> values to
/// <c>Input</c>; the node extracts a key and a side (request vs response) from each payload
/// via the injected selectors, pairs a request with its matching response by key, and
/// broadcasts a <c>FlowMessage&lt;FlowCorrelationOutcome&lt;TInput&gt;&gt;</c> on
/// <c>Output</c> carrying either a typed match or timeout outcome. Invalid
/// keys/sides, duplicate sides, selector failures, and pending-capacity overflow
/// travel on <c>Output</c> as <see cref="FlowError"/> messages while diagnostics
/// use <c>Events</c>; the node keeps processing.
/// </summary>
internal sealed class CorrelationNodeRuntime<TInput>
    : FlowNode<TInput, FlowCorrelationOutcome<TInput>>
{
    private readonly CorrelationRoutingOptions _options;
    private readonly Func<TInput, string?> _keySelector;
    private readonly Func<TInput, string?> _sideSelector;
    private readonly string? _engineName;
    private readonly TimeProvider _clock;
    private readonly StringComparer _comparer;
    private readonly string _requestSide;
    private readonly string _responseSide;
    private readonly TimeSpan _timeout;
    private readonly object _gate = new();
    private readonly CorrelationPendingStore<TInput> _pending;
    private readonly ActionBlock<Func<Task>> _timerEmissions;
    private ITimer? _timer;
    private long _timerVersion;
    private bool _acceptTimerEmissions = true;

    public CorrelationNodeRuntime(
        CorrelationRoutingOptions options,
        Func<TInput, string?> keySelector,
        Func<TInput, string?> sideSelector,
        string? engineName = null,
        TimeProvider? clock = null)
        : this(ValidateOptions(options), keySelector, sideSelector, engineName, clock)
    {
    }

    private CorrelationNodeRuntime(
        ValidatedOptions options,
        Func<TInput, string?> keySelector,
        Func<TInput, string?> sideSelector,
        string? engineName,
        TimeProvider? clock)
        : base(options.FlowNodeOptions)
    {
        _options = options.CorrelationOptions;
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _sideSelector = sideSelector ?? throw new ArgumentNullException(nameof(sideSelector));
        _engineName = engineName;
        _clock = clock ?? TimeProvider.System;

        _comparer = options.CorrelationOptions.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        _requestSide = options.RequestSide;
        _responseSide = options.ResponseSide;
        _timeout = TimeSpan.FromMilliseconds(options.CorrelationOptions.TimeoutMilliseconds);
        _pending = new CorrelationPendingStore<TInput>(
            _comparer,
            options.CorrelationOptions.MaxPending);
        _timerEmissions = new ActionBlock<Func<Task>>(
            emit => emit(),
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.FlowNodeOptions.OutputCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
    }

    protected override async Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var emissions = new List<Func<Task>>();
        lock (_gate)
        {
            try
            {
                var now = _clock.GetUtcNow();
                ExpireDue(now, force: false, emissions);
                Correlate(message, now, emissions);
            }
            catch (CorrelationException exception)
            {
                QueueCorrelationError(
                    exception.Code,
                    exception.Message,
                    exception.InnerException,
                    emissions,
                    exception.Key,
                    exception.Side,
                    message);
            }
            catch (Exception exception)
            {
                QueueCorrelationError(
                    RoutingErrorCodes.CorrelationKeyFailed,
                    $"flow.correlation failed: {exception.Message}",
                    exception,
                    emissions,
                    source: message);
            }
            finally
            {
                ScheduleTimer(_clock.GetUtcNow());
            }
        }

        foreach (var emit in emissions)
        {
            await emit().ConfigureAwait(false);
        }
    }

    /// <summary>Flushes remaining pending inputs as timeouts when the input drains.</summary>
    protected override async ValueTask OnInputCompletedAsync()
    {
        var emissions = new List<Func<Task>>();
        lock (_gate)
        {
            _acceptTimerEmissions = false;
            CancelTimer();
            ExpireDue(_clock.GetUtcNow(), force: true, emissions);
        }

        _timerEmissions.Complete();
        await _timerEmissions.Completion.ConfigureAwait(false);

        foreach (var emit in emissions)
        {
            await emit().ConfigureAwait(false);
        }
    }

    protected override async ValueTask OnDisposeAsync()
    {
        lock (_gate)
        {
            _acceptTimerEmissions = false;
            CancelTimer();
        }

        _timerEmissions.Complete();
        try
        {
            await _timerEmissions.Completion.ConfigureAwait(false);
        }
        catch
        {
            // Node completion remains the authoritative fault surface.
        }
    }

    // Must be called under _gate. Queues emit actions to run after the lock is released.
    private void Correlate(
        FlowMessage<TInput> message,
        DateTimeOffset now,
        List<Func<Task>> emissions)
    {
        var item = Evaluate(message.Value);
        if (!TryNormalizeSide(item.Side, out var side))
        {
            QueueCorrelationError(
                RoutingErrorCodes.CorrelationInvalidSide,
                $"flow.correlation side '{item.Side}' is not supported.",
                null,
                emissions,
                item.Key,
                item.Side,
                message);
            return;
        }

        if (!TryGetOrCreatePending(item.Key, out var pending, out var created))
        {
            QueueCorrelationError(
                RoutingErrorCodes.CorrelationCapacityExceeded,
                $"flow.correlation maxPending limit reached; key '{item.Key}' was not tracked.",
                null,
                emissions,
                item.Key,
                side,
                message);
            return;
        }

        var entry = new CorrelationPendingEntry<TInput>(message, side, now);
        if (pending.Get(side, _comparer) is { } existing)
        {
            entry = entry with { ReceivedAt = existing.ReceivedAt };
            TryEmitDuplicateSideDiagnostic(item.Key, side);
        }

        pending.Set(side, entry, _requestSide, _comparer);
        if (created)
        {
            _pending.TrackDeadline(item.Key, entry.ReceivedAt);
        }

        if (pending.Request is null || pending.Response is null)
        {
            return;
        }

        _pending.Remove(item.Key);
        QueueMatch(item.Key, pending.Request, pending.Response, now, emissions);
    }

    // Must be called under _gate.
    private void ExpireDue(DateTimeOffset now, bool force, List<Func<Task>> emissions)
    {
        foreach (var expired in _pending.TakeExpired(now, _timeout, force))
        {
            foreach (var entry in expired.Pending.Entries)
                QueueTimeout(expired.Key, entry, now, emissions);
        }
    }

    private CorrelationItem Evaluate(TInput value)
    {
        string? key;
        try
        {
            key = _keySelector(value);
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

        string? side;
        try
        {
            side = _sideSelector(value);
        }
        catch (Exception exception)
        {
            throw new CorrelationException(
                RoutingErrorCodes.CorrelationSideFailed,
                $"flow.correlation failed to evaluate side: {exception.Message}",
                exception,
                key);
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
        out CorrelationPendingPair<TInput> pending,
        out bool created)
        => _pending.TryGetOrCreate(key, out pending, out created);

    private void QueueMatch(
        string key,
        CorrelationPendingEntry<TInput> request,
        CorrelationPendingEntry<TInput> response,
        DateTimeOffset now,
        List<Func<Task>> emissions)
    {
        var match = new FlowCorrelationMatch<TInput>
        {
            Key = key,
            Request = request.Message.Value,
            Response = response.Message.Value,
            RequestReceivedAt = request.ReceivedAt,
            ResponseReceivedAt = response.ReceivedAt,
            MatchedAt = now,
            Elapsed = now - (request.ReceivedAt <= response.ReceivedAt
                ? request.ReceivedAt
                : response.ReceivedAt)
        };
        var pendingCount = _pending.Count;
        emissions.Add(async () =>
        {
            // The matched pair carries the request message's correlation id forward.
            await EmitAsync(
                    request.Message.With<FlowCorrelationOutcome<TInput>>(
                        new FlowCorrelationMatchedOutcome<TInput> { Match = match }),
                    Stopping)
                .ConfigureAwait(false);
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = request.Message.CorrelationId,
                Name = RoutingDiagnosticNames.CorrelationMatched,
                Level = FlowEventLevel.Information,
                Message = "flow.correlation matched pair.",
                Attributes = CreateAttributes(pendingCount, key)
            });
        });
    }

    private void QueueTimeout(
        string key,
        CorrelationPendingEntry<TInput> entry,
        DateTimeOffset now,
        List<Func<Task>> emissions)
    {
        var timeout = new FlowCorrelationTimeout<TInput>
        {
            Key = key,
            Side = entry.Side,
            Value = entry.Message.Value,
            ReceivedAt = entry.ReceivedAt,
            TimedOutAt = now,
            Timeout = _timeout
        };
        var pendingCount = _pending.Count;
        emissions.Add(async () =>
        {
            await EmitAsync(
                    entry.Message.With<FlowCorrelationOutcome<TInput>>(
                        new FlowCorrelationTimedOutOutcome<TInput> { Timeout = timeout }),
                    Stopping)
                .ConfigureAwait(false);
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = entry.Message.CorrelationId,
                Name = RoutingDiagnosticNames.CorrelationTimedOut,
                Level = FlowEventLevel.Warning,
                Message = "flow.correlation emitted timeout.",
                Attributes = CreateAttributes(pendingCount, key, entry.Side)
            });
        });
    }

    // Must be called under _gate.
    private void ScheduleTimer(DateTimeOffset now)
    {
        CancelTimer();
        _timerVersion++;
        if (_pending.Count == 0)
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
        _timer = _clock.CreateTimer(OnTimer, version, delay, Timeout.InfiniteTimeSpan);
    }

    // Must be called under _gate.
    private void CancelTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTimer(object? state)
    {
        var version = (long)state!;
        var emissions = new List<Func<Task>>();
        lock (_gate)
        {
            if (!_acceptTimerEmissions || version != _timerVersion)
            {
                return;
            }

            ExpireDue(_clock.GetUtcNow(), force: false, emissions);
            ScheduleTimer(_clock.GetUtcNow());
        }

        foreach (var emit in emissions)
        {
            if (!_timerEmissions.Post(emit))
            {
                Fault(new InvalidOperationException(
                    "flow.correlation timer emission capacity was exhausted."));
                return;
            }
        }
    }

    // Must be called under _gate.
    private void TryEmitDuplicateSideDiagnostic(string key, string side)
        => EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = RoutingDiagnosticNames.CorrelationDuplicateSide,
            Level = FlowEventLevel.Warning,
            Message = $"flow.correlation replaced duplicate side '{side}' for key '{key}'.",
            Attributes = CreateAttributes(_pending.Count, key, side)
        });

    // Must be called under _gate (reads _pending.Count via the passed count).
    private void QueueCorrelationError(
        int code,
        string message,
        Exception? exception,
        List<Func<Task>> emissions,
        string? key = null,
        string? side = null,
        FlowMessage<TInput>? source = null)
    {
        var details = JsonSerializer.SerializeToElement(new
        {
            legacyCode = code,
            context = CreateErrorContext(key, side),
            exceptionType = exception?.GetType().FullName
        });
        var error = new FlowError(
            RoutingErrorCodeNames.OperationFailed,
            message,
            "routing",
            exception is TimeoutException,
            details);
        var output = source is null
            ? FlowMessage.CreateError<FlowCorrelationOutcome<TInput>>(error)
            : source.WithError<FlowCorrelationOutcome<TInput>>(error);
        var @event = new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source?.CorrelationId,
            Name = RoutingDiagnosticNames.CorrelationFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateAttributes(_pending.Count, key, side)
        };
        emissions.Add(async () =>
        {
            await EmitAsync(output, Stopping).ConfigureAwait(false);
            EmitEvent(@event);
        });
    }

    private Dictionary<string, object?> CreateAttributes(
        int pendingCount,
        string? key = null,
        string? side = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = _options.InputType,
            ["engine"] = _engineName,
            ["caseSensitive"] = _options.CaseSensitive,
            ["timeoutMilliseconds"] = _options.TimeoutMilliseconds,
            ["maxPending"] = _options.MaxPending,
            ["pendingCount"] = pendingCount
        };

        if (!string.IsNullOrWhiteSpace(key))
        {
            attributes["key"] = key;
        }

        if (!string.IsNullOrWhiteSpace(side))
        {
            attributes["side"] = side;
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

    private string CreateErrorContext(string? key = null, string? side = null)
    {
        var values = new List<string>
        {
            $"inputType={_options.InputType}",
            $"engine={_engineName}",
            $"timeoutMilliseconds={_options.TimeoutMilliseconds}",
            $"maxPending={_options.MaxPending}"
        };

        if (!string.IsNullOrWhiteSpace(key))
        {
            values.Add($"key={key}");
        }

        if (!string.IsNullOrWhiteSpace(side))
        {
            values.Add($"side={side}");
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

    private sealed record CorrelationItem(string Key, string Side);

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

    private static ValidatedOptions ValidateOptions(CorrelationRoutingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new ArgumentException(
                "flow.correlation option 'inputType' cannot be empty.", nameof(options));
        }

        if (options.TimeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.correlation option 'timeoutMilliseconds' must be greater than zero.");
        }

        if (options.MaxPending <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.correlation option 'maxPending' must be greater than zero.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "flow.correlation option 'boundedCapacity' must be greater than zero.");
        }

        var requestSide = options.RequestSide?.Trim();
        if (string.IsNullOrWhiteSpace(requestSide))
        {
            throw new ArgumentException(
                "flow.correlation option 'requestSide' cannot be empty.", nameof(options));
        }

        var responseSide = options.ResponseSide?.Trim();
        if (string.IsNullOrWhiteSpace(responseSide))
        {
            throw new ArgumentException(
                "flow.correlation option 'responseSide' cannot be empty.", nameof(options));
        }

        var comparer = options.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        if (comparer.Equals(requestSide, responseSide))
        {
            throw new ArgumentException(
                "flow.correlation request side and response side must be different.",
                nameof(options));
        }

        return new ValidatedOptions(options, requestSide, responseSide);
    }

    private sealed class ValidatedOptions(
        CorrelationRoutingOptions correlationOptions,
        string requestSide,
        string responseSide)
    {
        public CorrelationRoutingOptions CorrelationOptions { get; } = correlationOptions;

        public string RequestSide { get; } = requestSide;

        public string ResponseSide { get; } = responseSide;

        public FlowNodeOptions FlowNodeOptions { get; } = new()
        {
            InputCapacity = correlationOptions.BoundedCapacity
        };
    }
}
