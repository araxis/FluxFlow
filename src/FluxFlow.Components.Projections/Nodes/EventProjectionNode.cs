using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Diagnostics;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Projections.Nodes;

/// <summary>
/// Folds ordered projection events into snapshots and emits successes and
/// expected failures through one normal result output.
/// </summary>
public sealed class EventProjectionNode : FlowNode<ProjectionEvent, EventProjectionSnapshot>
{
    private readonly EventProjectionOptions _options;
    private readonly EventFilter _filter;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _rateWindow;
    private readonly Queue<DateTimeOffset> _rateSamples = new();
    private long _observedCount;
    private long _matchedCount;
    private DateTimeOffset? _firstMatchedAt;
    private DateTimeOffset? _lastMatchedAt;
    private EventSummary? _latest;
    private FlowMessage<ProjectionEvent>? _lastMatchedMessage;
    public EventProjectionNode(
        EventProjectionOptions? options = null,
        TimeProvider? clock = null)
        : base(CreateNodeOptions(options))
    {
        _options = ResolveOptions(options);
        _filter = _options.Filter;
        _clock = clock ?? TimeProvider.System;
        _rateWindow = TimeSpan.FromSeconds(_options.RateWindowSeconds);
    }

    public async Task CompleteWithFinalSnapshotAsync()
    {
        Complete();
        await Completion.ConfigureAwait(false);
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage<ProjectionEvent> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
        {
            await EmitAsync(
                message.WithError<EventProjectionSnapshot>(message.Error!),
                Stopping).ConfigureAwait(false);
            return;
        }

        var timestamp = _clock.GetUtcNow();
        _observedCount++;

        try
        {
            var projectionEvent = message.Value ?? throw new InvalidOperationException(
                "event.projection requires a projection event input.");
            if (!EventFilterMatcher.IsMatch(projectionEvent, _filter))
                return;

            _matchedCount++;
            _firstMatchedAt ??= projectionEvent.Timestamp;
            _lastMatchedAt = projectionEvent.Timestamp;
            _latest = CreateSummary(projectionEvent);
            _lastMatchedMessage = message;
            AddRateSample(projectionEvent.Timestamp);

            var snapshot = CreateSnapshot(timestamp, projectionEvent.Timestamp);
            if (_options.EmitEveryMatch)
            {
                await EmitAsync(message.With(snapshot), Stopping).ConfigureAwait(false);
            }

            PublishEvent(
                message.CorrelationId,
                timestamp,
                ProjectionDiagnosticNames.ProjectionUpdated,
                FlowEventLevel.Information,
                "event.projection updated snapshot.",
                ProjectionResultKinds.Snapshot,
                snapshot,
                isError: false);
        }
        catch (Exception exception)
        {
            await PublishFailureAsync(message, timestamp, exception).ConfigureAwait(false);
        }
    }

    private async Task PublishFailureAsync(
        FlowMessage<ProjectionEvent> message,
        DateTimeOffset timestamp,
        Exception exception)
    {
        var error = new DataFlowError(
            ProjectionErrorCodeNames.ProjectionFailed,
            $"event.projection failed: {exception.Message}",
            category: "Projections",
            isTransient: false,
            details: CreateErrorDetails(message.Value, exception));
        await EmitAsync(
            message.WithError<EventProjectionSnapshot>(error),
            Stopping).ConfigureAwait(false);
        PublishEvent(
            message.CorrelationId,
            timestamp,
            ProjectionDiagnosticNames.ProjectionFailed,
            FlowEventLevel.Warning,
            error.Message,
            ProjectionResultKinds.ProjectionFailed,
            snapshot: null,
            isError: true);
    }

    private async ValueTask PublishFinalSnapshotAsync()
    {
        if (!_options.EmitFinalSnapshot)
            return;

        var timestamp = _clock.GetUtcNow();
        var snapshot = CreateSnapshot(timestamp, _lastMatchedAt ?? timestamp);
        var output = _lastMatchedMessage is null
            ? FlowMessage.Create(snapshot)
            : _lastMatchedMessage.With(snapshot);
        await EmitAsync(output, Stopping).ConfigureAwait(false);
        PublishEvent(
            output.CorrelationId,
            timestamp,
            ProjectionDiagnosticNames.ProjectionUpdated,
            FlowEventLevel.Information,
            "event.projection emitted final snapshot.",
            ProjectionResultKinds.FinalSnapshot,
            snapshot,
            isError: false);
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
            Filter = _filter
        };

    private EventSummary CreateSummary(ProjectionEvent projectionEvent)
        => new()
        {
            Timestamp = projectionEvent.Timestamp,
            Type = projectionEvent.Type,
            Source = projectionEvent.Source,
            SourceNodeId = projectionEvent.SourceNodeId,
            Subject = projectionEvent.Subject,
            Status = projectionEvent.Status,
            Channel = projectionEvent.Channel,
            PayloadBytes = projectionEvent.PayloadBytes,
            PayloadPreview = Truncate(projectionEvent.PayloadPreview),
            Attributes = projectionEvent.Attributes
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
            _rateSamples.Dequeue();
    }

    private double CalculateWindowRate(DateTimeOffset referenceTime)
    {
        TrimRateSamples(referenceTime);
        return _rateSamples.Count / _rateWindow.TotalSeconds;
    }

    private string? Truncate(string? value)
    {
        if (value is null || _options.MaxPreviewChars <= 0)
            return null;

        return value.Length <= _options.MaxPreviewChars
            ? value
            : value[.._options.MaxPreviewChars];
    }

    private void PublishEvent(
        CorrelationId? correlationId,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string message,
        string resultKind,
        EventProjectionSnapshot? snapshot,
        bool isError)
        => EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = correlationId,
            Name = name,
            Level = level,
            Message = message,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["currentRate"] = snapshot?.CurrentRate,
                ["isError"] = isError,
                ["latestChannel"] = snapshot?.Latest?.Channel,
                ["latestSubject"] = snapshot?.Latest?.Subject,
                ["latestType"] = snapshot?.Latest?.Type,
                ["matchedCount"] = snapshot?.MatchedCount ?? _matchedCount,
                ["name"] = snapshot?.Name ?? Normalize(_options.Name),
                ["observedCount"] = snapshot?.ObservedCount ?? _observedCount,
                ["resultKind"] = resultKind
            }
        });

    private static JsonElement CreateErrorDetails(
        ProjectionEvent? projectionEvent,
        Exception exception)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["channel"] = projectionEvent?.Channel,
            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
            ["legacyCode"] = ProjectionsErrorCodes.ProjectionFailed,
            ["source"] = projectionEvent?.Source,
            ["subject"] = projectionEvent?.Subject,
            ["type"] = projectionEvent?.Type
        });

    protected override ValueTask OnInputCompletedAsync()
        => PublishFinalSnapshotAsync();

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

        return resolved with { Filter = resolved.Filter ?? new EventFilter() };
    }

    private static FlowNodeOptions CreateNodeOptions(EventProjectionOptions? options)
    {
        var resolved = ResolveOptions(options);
        return new FlowNodeOptions
        {
            InputCapacity = resolved.BoundedCapacity,
            OutputCapacity = resolved.BoundedCapacity
        };
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
