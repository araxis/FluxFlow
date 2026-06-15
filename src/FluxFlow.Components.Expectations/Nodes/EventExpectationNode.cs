using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Diagnostics;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Expectations.Nodes;

public sealed class EventExpectationNode : FlowNodeBase, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly EventExpectationSettings _settings;
    private readonly TimeProvider _clock;
    private readonly EventExpectationNodeKind _kind;
    private readonly ActionBlock<FlowEvent> _input;
    private readonly BufferBlock<EventExpectationResult> _result;
    private readonly List<EventSummary> _observedEvents = [];
    private CancellationTokenSource? _timeoutCancellation;
    private Task? _timeoutTask;
    private bool _started;
    private bool _resolved;
    private bool _disposed;

    internal EventExpectationNode(
        EventExpectationSettings settings,
        TimeProvider clock,
        EventExpectationNodeKind kind)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _kind = kind;
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "event expectation bounded capacity must be greater than zero.");
        }

        _input = new ActionBlock<FlowEvent>(
            ProcessEvent,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = settings.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _result = new BufferBlock<EventExpectationResult>(
            new DataflowBlockOptions { BoundedCapacity = settings.BoundedCapacity });
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<FlowEvent> Input => _input;

    public ISourceBlock<EventExpectationResult> Result => _result;

    public int ObservedEventCount
    {
        get
        {
            lock (_stateLock)
            {
                return _observedEvents.Count;
            }
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            if (_started)
            {
                throw new InvalidOperationException("event expectation node has already started.");
            }

            _started = true;
            if (_settings.Timeout.HasValue)
            {
                _timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _timeoutTask = RunTimeoutAsync(_timeoutCancellation.Token);
            }
        }

        return Task.CompletedTask;
    }

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        CancelTimeout();
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_result).Fault(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Complete();
        CancelTimeout();

        try
        {
            await _input.Completion.ConfigureAwait(false);
            if (_timeoutTask is not null)
            {
                await _timeoutTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _timeoutCancellation?.Dispose();
        }
    }

    protected override void OnNodeCompleted()
    {
        CancelTimeout();
        ResolveOnCompletion();
        _result.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        CancelTimeout();
        ((IDataflowBlock)_result).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private Task ProcessEvent(FlowEvent flowEvent)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(flowEvent);
            var summary = CreateSummary(flowEvent);
            RememberObservedEvent(summary);

            if (!EventFilterMatcher.IsMatch(flowEvent, _settings.Filter))
            {
                return Task.CompletedTask;
            }

            if (_kind == EventExpectationNodeKind.Expect)
            {
                Resolve(
                    satisfied: true,
                    matched: true,
                    timedOut: false,
                    matchedEvent: summary,
                    reason: "Matching event observed.");
            }
            else
            {
                Resolve(
                    satisfied: false,
                    matched: true,
                    timedOut: false,
                    matchedEvent: summary,
                    reason: "Guarded event observed.");
            }
        }
        catch (Exception exception)
        {
            TryReportError(
                ExpectationsErrorCodes.EvaluationFailed,
                $"event expectation failed: {exception.Message}",
                exception,
                CreateEventContext(flowEvent));
            TryEmitDiagnostic(
                ExpectationDiagnosticNames.EvaluationFailed,
                FlowDiagnosticLevel.Error,
                "event expectation failed.",
                exception,
                CreateEventAttributes(flowEvent));
        }

        return Task.CompletedTask;
    }

    private async Task RunTimeoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_settings.Timeout!.Value, _clock, cancellationToken).ConfigureAwait(false);
            if (_kind == EventExpectationNodeKind.Expect)
            {
                Resolve(
                    satisfied: false,
                    matched: false,
                    timedOut: true,
                    matchedEvent: null,
                    reason: "Expected event was not observed before timeout.");
            }
            else
            {
                Resolve(
                    satisfied: true,
                    matched: false,
                    timedOut: true,
                    matchedEvent: null,
                    reason: "Guard timeout completed without a matching event.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ResolveOnCompletion()
    {
        if (_kind == EventExpectationNodeKind.Expect)
        {
            Resolve(
                satisfied: false,
                matched: false,
                timedOut: false,
                matchedEvent: null,
                reason: "Input completed before a matching event was observed.");
        }
        else
        {
            Resolve(
                satisfied: true,
                matched: false,
                timedOut: false,
                matchedEvent: null,
                reason: "Input completed without a matching event.");
        }
    }

    private bool Resolve(
        bool satisfied,
        bool matched,
        bool timedOut,
        EventSummary? matchedEvent,
        string reason)
    {
        EventExpectationResult result;
        lock (_stateLock)
        {
            if (_resolved)
            {
                return false;
            }

            _resolved = true;
            result = new EventExpectationResult
            {
                EvaluatedAt = _clock.GetUtcNow(),
                Name = _settings.Name,
                Kind = _kind == EventExpectationNodeKind.Expect
                    ? EventExpectationResultKind.Expect
                    : EventExpectationResultKind.Guard,
                Satisfied = satisfied,
                Matched = matched,
                TimedOut = timedOut,
                MatchedEvent = matchedEvent,
                ObservedEvents = _observedEvents.ToArray(),
                Filter = CopyFilter(_settings.Filter),
                Reason = reason
            };
        }

        EmitResult(result);
        EmitResultDiagnostic(result);
        return true;
    }

    private void EmitResult(EventExpectationResult result)
    {
        if (_result.Post(result))
        {
            return;
        }

        var message = "event expectation result output was full; result was dropped.";
        TryReportError(
            ExpectationsErrorCodes.ResultDropped,
            message,
            context: CreateResultContext(result));
        TryEmitDiagnostic(
            ExpectationDiagnosticNames.ResultDropped,
            FlowDiagnosticLevel.Warning,
            message,
            attributes: CreateResultAttributes(result));
    }

    private void EmitResultDiagnostic(EventExpectationResult result)
    {
        var name = result.Matched
            ? ExpectationDiagnosticNames.Matched
            : result.TimedOut
                ? ExpectationDiagnosticNames.TimedOut
                : ExpectationDiagnosticNames.Completed;
        TryEmitDiagnostic(
            name,
            result.Satisfied ? FlowDiagnosticLevel.Information : FlowDiagnosticLevel.Warning,
            result.Reason,
            attributes: CreateResultAttributes(result));
    }

    private void RememberObservedEvent(EventSummary summary)
    {
        if (_settings.MaxObservedEvents == 0)
        {
            return;
        }

        lock (_stateLock)
        {
            _observedEvents.Add(summary);
            while (_observedEvents.Count > _settings.MaxObservedEvents)
            {
                _observedEvents.RemoveAt(0);
            }
        }
    }

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

    private string? Truncate(string? value)
    {
        if (value is null || _settings.MaxPreviewChars <= 0)
        {
            return null;
        }

        return value.Length <= _settings.MaxPreviewChars
            ? value
            : value[.._settings.MaxPreviewChars];
    }

    private void CancelTimeout()
    {
        lock (_stateLock)
        {
            _timeoutCancellation?.Cancel();
        }
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

    private static Dictionary<string, object?> CreateResultAttributes(EventExpectationResult result)
        => new(StringComparer.Ordinal)
        {
            ["kind"] = result.Kind.ToString(),
            ["satisfied"] = result.Satisfied,
            ["matched"] = result.Matched,
            ["timedOut"] = result.TimedOut,
            ["observedCount"] = result.ObservedEvents.Count,
            ["matchedType"] = result.MatchedEvent?.Type,
            ["matchedSubject"] = result.MatchedEvent?.Subject,
            ["matchedChannel"] = result.MatchedEvent?.Channel
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

    private static string CreateResultContext(EventExpectationResult result)
        => string.Join(
            "; ",
            [
                $"kind={result.Kind}",
                $"satisfied={result.Satisfied}",
                $"matched={result.Matched}",
                $"timedOut={result.TimedOut}"
            ]);

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
}
