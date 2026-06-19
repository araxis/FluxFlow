using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Routing.Nodes;

/// <summary>
/// A standalone correlation node. Post <c>FlowMessage&lt;TInput&gt;</c> values to
/// <c>Input</c>; the node extracts a key and a side (request vs response) from each payload
/// via the injected selectors, pairs a request with its matching response by key, and
/// broadcasts a <c>FlowMessage&lt;FlowCorrelationMatch&lt;TInput&gt;&gt;</c> on <c>Output</c>
/// (the matched pair carries the request message's correlation id). Pending inputs that go
/// unmatched past the configured timeout — observed against the injected
/// <see cref="TimeProvider"/>, before the next input or when the node completes — are
/// broadcast on <c>Timeouts</c>. Invalid keys/sides, duplicate sides, selector failures, and
/// pending-capacity overflow surface on <c>Errors</c>/diagnostics and the node keeps
/// processing. Works with nothing but <c>new FlowCorrelationNode&lt;T&gt;(options, key, side)</c>
/// — no engine.
/// </summary>
public sealed class FlowCorrelationNode<TInput> : FlowNode<TInput, FlowCorrelationMatch<TInput>>
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
    private readonly Dictionary<string, PendingPair> _pending;
    private readonly Queue<CorrelationDeadline> _deadlines = new();
    private readonly BroadcastBlock<FlowMessage<FlowCorrelationTimeout<TInput>>> _timeouts;
    private ITimer? _timer;
    private long _timerVersion;

    public FlowCorrelationNode(
        CorrelationRoutingOptions options,
        Func<TInput, string?> keySelector,
        Func<TInput, string?> sideSelector,
        string? engineName = null,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (options ?? throw new ArgumentNullException(nameof(options))).BoundedCapacity
        })
    {
        _options = options;
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _sideSelector = sideSelector ?? throw new ArgumentNullException(nameof(sideSelector));
        _engineName = engineName;
        _clock = clock ?? TimeProvider.System;
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
        _timeouts = AddOutput<FlowMessage<FlowCorrelationTimeout<TInput>>>();
    }

    /// <summary>Matched request/response pairs, carrying the request's correlation id (primary).</summary>
    public ISourceBlock<FlowMessage<FlowCorrelationMatch<TInput>>> Matched => Output;

    /// <summary>Unmatched pending inputs that timed out, carrying their correlation id.</summary>
    public ISourceBlock<FlowMessage<FlowCorrelationTimeout<TInput>>> Timeouts => _timeouts;

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var emissions = new List<Action>();
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
            finally
            {
                ScheduleTimer(_clock.GetUtcNow());
            }
        }

        foreach (var emit in emissions)
        {
            emit();
        }

        return Task.CompletedTask;
    }

    /// <summary>Flushes remaining pending inputs as timeouts when the input drains.</summary>
    protected override ValueTask OnInputCompletedAsync()
    {
        var emissions = new List<Action>();
        lock (_gate)
        {
            CancelTimer();
            ExpireDue(_clock.GetUtcNow(), force: true, emissions);
        }

        foreach (var emit in emissions)
        {
            emit();
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

    // Must be called under _gate. Queues emit actions to run after the lock is released.
    private void Correlate(FlowMessage<TInput> message, DateTimeOffset now, List<Action> emissions)
    {
        var item = Evaluate(message.Payload);
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

        var entry = new PendingEntry(message, side, now);
        if (pending.Get(side, _comparer) is { } existing)
        {
            entry = entry with { ReceivedAt = existing.ReceivedAt };
            TryEmitDuplicateSideDiagnostic(item.Key, side);
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
        QueueMatch(item.Key, pending.Request, pending.Response, now, emissions);
    }

    // Must be called under _gate.
    private void ExpireDue(DateTimeOffset now, bool force, List<Action> emissions)
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
                QueueTimeout(deadline.Key, entry, now, emissions);
            }
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

    private bool TryGetOrCreatePending(string key, out PendingPair pending, out bool created)
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

    private void QueueMatch(
        string key,
        PendingEntry request,
        PendingEntry response,
        DateTimeOffset now,
        List<Action> emissions)
    {
        var match = new FlowCorrelationMatch<TInput>
        {
            Key = key,
            Request = request.Message.Payload,
            Response = response.Message.Payload,
            RequestReceivedAt = request.ReceivedAt,
            ResponseReceivedAt = response.ReceivedAt,
            MatchedAt = now,
            Elapsed = now - (request.ReceivedAt <= response.ReceivedAt
                ? request.ReceivedAt
                : response.ReceivedAt)
        };
        var pendingCount = _pending.Count;
        emissions.Add(() =>
        {
            // The matched pair carries the request message's correlation id forward.
            Emit(request.Message.With(match));
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
        PendingEntry entry,
        DateTimeOffset now,
        List<Action> emissions)
    {
        var timeout = new FlowCorrelationTimeout<TInput>
        {
            Key = key,
            Side = entry.Side,
            Value = entry.Message.Payload,
            ReceivedAt = entry.ReceivedAt,
            TimedOutAt = now,
            Timeout = _timeout
        };
        var pendingCount = _pending.Count;
        emissions.Add(() =>
        {
            _timeouts.Post(entry.Message.With(timeout));
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

        var dueAt = GetNextDueAt();
        if (dueAt is null)
        {
            return;
        }

        var delay = dueAt.Value <= now ? TimeSpan.Zero : dueAt.Value - now;
        var version = _timerVersion;
        _timer = _clock.CreateTimer(OnTimer, version, delay, Timeout.InfiniteTimeSpan);
    }

    // Must be called under _gate.
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

    // Must be called under _gate.
    private void CancelTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTimer(object? state)
    {
        var version = (long)state!;
        var emissions = new List<Action>();
        lock (_gate)
        {
            if (version != _timerVersion)
            {
                return;
            }

            ExpireDue(_clock.GetUtcNow(), force: false, emissions);
            ScheduleTimer(_clock.GetUtcNow());
        }

        foreach (var emit in emissions)
        {
            emit();
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
    private void ReportCorrelationError(
        int code,
        string message,
        Exception? exception,
        string? key = null,
        string? side = null)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            Code = code,
            Message = message,
            Context = CreateErrorContext(key, side),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = RoutingDiagnosticNames.CorrelationFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateAttributes(_pending.Count, key, side)
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

    private sealed record CorrelationDeadline(string Key, DateTimeOffset ReceivedAt);

    private sealed record PendingEntry(
        FlowMessage<TInput> Message,
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
