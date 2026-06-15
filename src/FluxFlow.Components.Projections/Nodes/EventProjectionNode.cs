using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Diagnostics;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Projections.Nodes;

public sealed class EventProjectionNode : FlowNodeBase
{
    private readonly EventProjectionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _rateWindow;
    private readonly ActionBlock<FlowEvent> _input;
    private readonly BufferBlock<EventProjectionSnapshot> _output;
    private readonly Queue<DateTimeOffset> _rateSamples = new();
    private long _observedCount;
    private long _matchedCount;
    private DateTimeOffset? _firstMatchedAt;
    private DateTimeOffset? _lastMatchedAt;
    private EventSummary? _latest;

    internal EventProjectionNode(
        EventProjectionOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event.projection bounded capacity must be greater than zero.");
        }

        _rateWindow = TimeSpan.FromSeconds(options.RateWindowSeconds);
        var inputOptions = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity,
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        };
        _input = new ActionBlock<FlowEvent>(ProjectAsync, inputOptions);
        _output = new BufferBlock<EventProjectionSnapshot>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<FlowEvent> Input => _input;

    public ISourceBlock<EventProjectionSnapshot> Output => _output;

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_output).Fault(exception);
        }
    }

    protected override void OnNodeCompleted()
    {
        if (_options.EmitFinalSnapshot)
        {
            var timestamp = _timeProvider.GetUtcNow();
            EmitSnapshot(CreateSnapshot(timestamp, _lastMatchedAt ?? timestamp));
        }

        _output.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        ((IDataflowBlock)_output).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private Task ProjectAsync(FlowEvent flowEvent)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(flowEvent);
            _observedCount++;

            if (!EventFilterMatcher.IsMatch(flowEvent, _options.Filter))
            {
                return Task.CompletedTask;
            }

            _matchedCount++;
            _firstMatchedAt ??= flowEvent.Timestamp;
            _lastMatchedAt = flowEvent.Timestamp;
            _latest = CreateSummary(flowEvent);
            AddRateSample(flowEvent.Timestamp);

            var snapshot = CreateSnapshot(_timeProvider.GetUtcNow(), flowEvent.Timestamp);
            if (_options.EmitEveryMatch)
            {
                EmitSnapshot(snapshot);
            }

            TryEmitDiagnostic(
                ProjectionDiagnosticNames.ProjectionUpdated,
                message: "event.projection updated snapshot.",
                attributes: CreateSnapshotAttributes(snapshot));
        }
        catch (Exception exception)
        {
            TryReportError(
                ProjectionsErrorCodes.ProjectionFailed,
                $"event.projection failed: {exception.Message}",
                exception,
                CreateEventContext(flowEvent));
            TryEmitDiagnostic(
                ProjectionDiagnosticNames.ProjectionFailed,
                FlowDiagnosticLevel.Error,
                "event.projection failed.",
                exception,
                CreateEventAttributes(flowEvent));
        }

        return Task.CompletedTask;
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

    private EventSummary CreateSummary(FlowEvent flowEvent)
        => new()
        {
            Timestamp = flowEvent.Timestamp,
            Type = flowEvent.Type,
            Source = flowEvent.Source,
            SourceNodeId = flowEvent.SourceNodeId?.ToString(),
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

    private void EmitSnapshot(EventProjectionSnapshot snapshot)
    {
        if (_output.Post(snapshot))
        {
            return;
        }

        var message = "event.projection snapshot output was full; snapshot was dropped.";
        TryReportError(
            ProjectionsErrorCodes.SnapshotDropped,
            message,
            context: $"matchedCount={snapshot.MatchedCount}; boundedCapacity={_options.BoundedCapacity}");
        TryEmitDiagnostic(
            ProjectionDiagnosticNames.SnapshotDropped,
            FlowDiagnosticLevel.Warning,
            message,
            attributes: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["matchedCount"] = snapshot.MatchedCount,
                ["boundedCapacity"] = _options.BoundedCapacity
            });
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

    private static Dictionary<string, object?> CreateEventAttributes(FlowEvent? flowEvent)
        => new(StringComparer.Ordinal)
        {
            ["type"] = flowEvent?.Type,
            ["source"] = flowEvent?.Source,
            ["subject"] = flowEvent?.Subject,
            ["channel"] = flowEvent?.Channel,
            ["status"] = flowEvent?.Status
        };

    private static string CreateEventContext(FlowEvent? flowEvent)
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
