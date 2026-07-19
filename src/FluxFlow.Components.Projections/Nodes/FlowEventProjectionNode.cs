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
public sealed class FlowEventProjectionNode : IFlowNode
{
    private readonly EventProjectionOptions _options;
    private readonly EventFilter _filter;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _rateWindow;
    private readonly Queue<DateTimeOffset> _rateSamples = new();
    private readonly ActionBlock<FlowMessage<ProjectionEvent>> _processor;
    private readonly BroadcastBlock<FlowMessage<FlowResult<EventProjectionSnapshot>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _observedCount;
    private long _matchedCount;
    private DateTimeOffset? _firstMatchedAt;
    private DateTimeOffset? _lastMatchedAt;
    private EventSummary? _latest;
    private FlowMessage<ProjectionEvent>? _lastMatchedMessage;
    private int _disposed;

    public FlowEventProjectionNode(
        EventProjectionOptions? options = null,
        TimeProvider? clock = null)
    {
        _options = ResolveOptions(options);
        _filter = _options.Filter;
        _clock = clock ?? TimeProvider.System;
        _rateWindow = TimeSpan.FromSeconds(_options.RateWindowSeconds);
        _processor = new ActionBlock<FlowMessage<ProjectionEvent>>(
            Process,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.BoundedCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<ProjectionEvent>> Input => _processor;

    public ISourceBlock<FlowMessage<FlowResult<EventProjectionSnapshot>>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public void Complete() => _processor.Complete();

    public async Task CompleteWithFinalSnapshotAsync()
    {
        Complete();
        await Completion.ConfigureAwait(false);
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ((IDataflowBlock)_processor).Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Complete();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative unexpected-fault surface.
        }
    }

    private void Process(FlowMessage<ProjectionEvent> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var timestamp = _clock.GetUtcNow();
        _observedCount++;

        try
        {
            var projectionEvent = message.Payload ?? throw new InvalidOperationException(
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
                _output.Post(message.With(FlowResult<EventProjectionSnapshot>.Success(
                    ProjectionResultKinds.Snapshot,
                    snapshot,
                    timestamp)));
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
            PublishFailure(message, timestamp, exception);
        }
    }

    private void PublishFailure(
        FlowMessage<ProjectionEvent> message,
        DateTimeOffset timestamp,
        Exception exception)
    {
        var error = new DataFlowError(
            ProjectionErrorCodeNames.ProjectionFailed,
            $"event.projection failed: {exception.Message}",
            category: "Projections",
            isTransient: false,
            details: CreateErrorDetails(message.Payload, exception));
        _output.Post(message.With(FlowResult<EventProjectionSnapshot>.Failure(
            ProjectionResultKinds.ProjectionFailed,
            error,
            timestamp)));
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

    private void PublishFinalSnapshot()
    {
        if (!_options.EmitFinalSnapshot)
            return;

        var timestamp = _clock.GetUtcNow();
        var snapshot = CreateSnapshot(timestamp, _lastMatchedAt ?? timestamp);
        var result = FlowResult<EventProjectionSnapshot>.Success(
            ProjectionResultKinds.FinalSnapshot,
            snapshot,
            timestamp);
        var output = _lastMatchedMessage is null
            ? FlowMessage.Create(result)
            : _lastMatchedMessage.With(result);
        _output.Post(output);
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
            Filter = CopyFilter(_filter)
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
            Attributes = CopyDictionary(projectionEvent.Attributes)
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
        CorrelationId correlationId,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string message,
        string resultKind,
        EventProjectionSnapshot? snapshot,
        bool isError)
        => _events.Post(new FlowEvent
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

    private static FlowValue CreateErrorDetails(
        ProjectionEvent? projectionEvent,
        Exception exception)
        => FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["channel"] = FlowValue.From(projectionEvent?.Channel ?? string.Empty),
            ["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name),
            ["legacyCode"] = FlowValue.From(ProjectionsErrorCodes.ProjectionFailed),
            ["source"] = FlowValue.From(projectionEvent?.Source ?? string.Empty),
            ["subject"] = FlowValue.From(projectionEvent?.Subject ?? string.Empty),
            ["type"] = FlowValue.From(projectionEvent?.Type ?? string.Empty)
        });

    private async Task MonitorCompletionAsync()
    {
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            PublishFinalSnapshot();
            _output.Complete();
            await _output.Completion.ConfigureAwait(false);
            _events.Complete();
            await _events.Completion.ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            try
            {
                ((IDataflowBlock)_output).Fault(exception);
            }
            catch
            {
                // The output may already be terminal.
            }

            _events.Complete();
            _completion.TrySetException(exception);
        }
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

        return resolved with { Filter = resolved.Filter ?? new EventFilter() };
    }

    private static EventFilter CopyFilter(EventFilter filter)
        => filter with { Attributes = CopyDictionary(filter.Attributes) };

    private static Dictionary<string, string> CopyDictionary(
        IReadOnlyDictionary<string, string>? source)
        => source is null
            ? []
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
