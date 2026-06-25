using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Diagnostics;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Projections.Nodes;

/// <summary>
/// A standalone event-projection node. Post a <c>FlowMessage&lt;ProjectionEvent&gt;</c>
/// to <c>Input</c>; the node folds every event that matches its configured
/// <see cref="EventFilter"/> into a running projection (observed/matched counts, a
/// rolling rate over a time window, first/last matched timestamps, and the latest
/// matching event summary) and broadcasts a
/// <c>FlowMessage&lt;EventProjectionSnapshot&gt;</c> on <c>Output</c> carrying the
/// triggering event's correlation id (failures on <c>Errors</c>, diagnostics on
/// <c>Events</c>). Works with nothing but <c>new EventProjectionNode(options)</c> — no
/// engine. Events are processed strictly in order on a single worker so the rolling
/// state stays consistent.
/// </summary>
public sealed class EventProjectionNode : FlowNode<ProjectionEvent, EventProjectionSnapshot>
{
    // Reference-identity sentinel posted through the ordered input pump to emit a
    // final snapshot once all real events have been folded in. Carrying the flush
    // in-band keeps it ordered behind every observed event without a kit hook.
    private static readonly ProjectionEvent FlushSentinel = new()
    {
        Timestamp = default,
        Type = "__flush__",
        Source = "__flush__"
    };

    private readonly EventProjectionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _rateWindow;
    private readonly Queue<DateTimeOffset> _rateSamples = new();
    private long _observedCount;
    private long _matchedCount;
    private DateTimeOffset? _firstMatchedAt;
    private DateTimeOffset? _lastMatchedAt;
    private EventSummary? _latest;
    private CorrelationId? _lastMatchedCorrelationId;

    public EventProjectionNode(
        EventProjectionOptions? options = null,
        TimeProvider? timeProvider = null)
        : this(ResolveOptions(options), timeProvider, validated: true)
    {
    }

    private EventProjectionNode(
        EventProjectionOptions options,
        TimeProvider? timeProvider,
        bool validated)
        : base(new FlowNodeOptions
        {
            InputCapacity = options.BoundedCapacity,
            MaxDegreeOfParallelism = 1
        })
    {
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _rateWindow = TimeSpan.FromSeconds(_options.RateWindowSeconds);
    }

    private static EventProjectionOptions ResolveOptions(EventProjectionOptions? options)
    {
        var resolved = options ?? new EventProjectionOptions();

        if (resolved.RateWindowSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event.projection option 'rateWindowSeconds' must be greater than zero.");
        }

        if (resolved.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event.projection option 'boundedCapacity' must be greater than zero.");
        }

        // A null filter means match-all (matcher and snapshot copy both expect a value).
        return resolved with { Filter = resolved.Filter ?? new EventFilter() };
    }

    /// <summary>
    /// Completes the node, emitting one final snapshot first when
    /// <see cref="EventProjectionOptions.EmitFinalSnapshot"/> is set. The flush is sent
    /// through the ordered input pump, so it lands behind every event already posted.
    /// </summary>
    public async Task CompleteWithFinalSnapshotAsync()
    {
        if (_options.EmitFinalSnapshot)
        {
            await Input.SendAsync(FlowMessage.Create(FlushSentinel)).ConfigureAwait(false);
        }

        Complete();
    }

    protected override Task ProcessAsync(FlowMessage<ProjectionEvent> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (ReferenceEquals(message.Payload, FlushSentinel))
        {
            EmitFinalSnapshot();
            return Task.CompletedTask;
        }

        var flowEvent = message.Payload;
        try
        {
            _observedCount++;

            if (!EventFilterMatcher.IsMatch(flowEvent, _options.Filter))
            {
                return Task.CompletedTask;
            }

            _matchedCount++;
            _firstMatchedAt ??= flowEvent.Timestamp;
            _lastMatchedAt = flowEvent.Timestamp;
            _latest = CreateSummary(flowEvent);
            _lastMatchedCorrelationId = message.CorrelationId;
            AddRateSample(flowEvent.Timestamp);

            var snapshot = CreateSnapshot(_timeProvider.GetUtcNow(), flowEvent.Timestamp);
            if (_options.EmitEveryMatch)
            {
                Emit(message.With(snapshot));
            }

            EmitEvent(new FlowEvent
            {
                Timestamp = _timeProvider.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = ProjectionDiagnosticNames.ProjectionUpdated,
                Level = FlowEventLevel.Information,
                Message = "event.projection updated snapshot.",
                Attributes = CreateSnapshotAttributes(snapshot)
            });
        }
        catch (Exception exception)
        {
            EmitError(new FlowError
            {
                Timestamp = _timeProvider.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Code = ProjectionsErrorCodes.ProjectionFailed,
                Message = $"event.projection failed: {exception.Message}",
                Context = CreateEventContext(flowEvent),
                Exception = exception
            });
            EmitEvent(new FlowEvent
            {
                Timestamp = _timeProvider.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = ProjectionDiagnosticNames.ProjectionFailed,
                Level = FlowEventLevel.Error,
                Message = "event.projection failed.",
                Attributes = CreateEventAttributes(flowEvent)
            });
        }

        return Task.CompletedTask;
    }

    private void EmitFinalSnapshot()
    {
        var timestamp = _timeProvider.GetUtcNow();
        var snapshot = CreateSnapshot(timestamp, _lastMatchedAt ?? timestamp);

        // The final snapshot rides the last matched event's correlation id when there
        // was a match; otherwise it is a fresh exchange (no event drove it).
        var message = _lastMatchedCorrelationId is { } correlationId
            ? new FlowMessage<EventProjectionSnapshot>(correlationId, snapshot)
            : FlowMessage.Create(snapshot);
        Emit(message);
    }

    private EventProjectionSnapshot CreateSnapshot(
        DateTimeOffset timestamp,
        DateTimeOffset rateReferenceTime)
        => new()
        {
            Timestamp = timestamp,
            Name = Normalize(_options.Name),
            ObservedCount = _observedCount,
            MatchedCount = _matchedCount,
            CurrentRate = CalculateWindowRate(rateReferenceTime),
            FirstMatchedAt = _firstMatchedAt,
            LastMatchedAt = _lastMatchedAt,
            Latest = _latest,
            Filter = CopyFilter(_options.Filter)
        };

    private EventSummary CreateSummary(ProjectionEvent flowEvent)
        => new()
        {
            Timestamp = flowEvent.Timestamp,
            Type = flowEvent.Type,
            Source = flowEvent.Source,
            SourceNodeId = flowEvent.SourceNodeId,
            Subject = flowEvent.Subject,
            Status = flowEvent.Status,
            Channel = flowEvent.Channel,
            PayloadBytes = flowEvent.PayloadBytes,
            PayloadPreview = Truncate(flowEvent.PayloadPreview),
            Attributes = CopyDictionary(flowEvent.Attributes)
        };

    private void AddRateSample(DateTimeOffset timestamp)
    {
        _rateSamples.Enqueue(timestamp);
        TrimRateSamples(timestamp);
    }

    private void TrimRateSamples(DateTimeOffset referenceTime)
    {
        var cutoff = referenceTime - _rateWindow;
        while (_rateSamples.TryPeek(out var first) && first < cutoff)
        {
            _rateSamples.Dequeue();
        }
    }

    private double CalculateWindowRate(DateTimeOffset referenceTime)
    {
        TrimRateSamples(referenceTime);
        return _rateSamples.Count / _rateWindow.TotalSeconds;
    }

    private string? Truncate(string? value)
    {
        if (value is null || _options.MaxPreviewChars <= 0)
        {
            return null;
        }

        return value.Length <= _options.MaxPreviewChars
            ? value
            : value[.._options.MaxPreviewChars];
    }

    private static EventFilter CopyFilter(EventFilter filter)
        => filter with
        {
            Attributes = CopyDictionary(filter.Attributes)
        };

    private static Dictionary<string, string> CopyDictionary(IReadOnlyDictionary<string, string>? source)
        => source is null
            ? []
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    private static Dictionary<string, object?> CreateSnapshotAttributes(EventProjectionSnapshot snapshot)
        => new(StringComparer.Ordinal)
        {
            ["name"] = snapshot.Name,
            ["observedCount"] = snapshot.ObservedCount,
            ["matchedCount"] = snapshot.MatchedCount,
            ["currentRate"] = snapshot.CurrentRate,
            ["latestType"] = snapshot.Latest?.Type,
            ["latestSubject"] = snapshot.Latest?.Subject,
            ["latestChannel"] = snapshot.Latest?.Channel
        };

    private static Dictionary<string, object?> CreateEventAttributes(ProjectionEvent? flowEvent)
        => new(StringComparer.Ordinal)
        {
            ["type"] = flowEvent?.Type,
            ["source"] = flowEvent?.Source,
            ["subject"] = flowEvent?.Subject,
            ["channel"] = flowEvent?.Channel,
            ["status"] = flowEvent?.Status
        };

    private static string CreateEventContext(ProjectionEvent? flowEvent)
    {
        if (flowEvent is null)
        {
            return string.Empty;
        }

        var values = new List<string>
        {
            $"type={flowEvent.Type}",
            $"source={flowEvent.Source}"
        };

        if (!string.IsNullOrWhiteSpace(flowEvent.Subject))
        {
            values.Add($"subject={flowEvent.Subject}");
        }

        if (!string.IsNullOrWhiteSpace(flowEvent.Channel))
        {
            values.Add($"channel={flowEvent.Channel}");
        }

        return string.Join("; ", values);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
