using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Diagnostics;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Expectations.Nodes;

/// <summary>
/// Resolves a projection-event expectation exactly once through a normal
/// <see cref="FlowResult{T}"/> output. Rule outcomes, timeout, and input
/// completion are successful variants; expected evaluation failures are error
/// variants on the same output.
/// </summary>
public sealed class FlowEventExpectationNode : IFlowNode
{
    private readonly object _gate = new();
    private readonly EventExpectationOptions _options;
    private readonly EventFilter _filter;
    private readonly TimeProvider _clock;
    private readonly EventExpectationNodeKind _kind;
    private readonly List<EventSummary> _observedEvents = [];
    private readonly ActionBlock<FlowMessage<ProjectionEvent>> _processor;
    private readonly BroadcastBlock<FlowMessage<FlowResult<EventExpectationResult>>> _output =
        new(static message => message);
    private readonly BroadcastBlock<FlowEvent> _events = new(static @event => @event);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ITimer? _timeoutTimer;
    private FlowMessage<ProjectionEvent>? _lastMessage;
    private bool _resolved;
    private int _disposed;

    public FlowEventExpectationNode(
        EventExpectationOptions? options = null,
        TimeProvider? clock = null)
    {
        _options = ResolveOptions(options);
        _filter = _options.Filter!;
        _clock = clock ?? TimeProvider.System;
        _kind = _options.Kind;
        _processor = new ActionBlock<FlowMessage<ProjectionEvent>>(
            Process,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.BoundedCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });

        if (_options.TimeoutMilliseconds is { } milliseconds)
        {
            _timeoutTimer = _clock.CreateTimer(
                static state => ((FlowEventExpectationNode)state!).ResolveOnTimeout(),
                this,
                TimeSpan.FromMilliseconds(milliseconds),
                Timeout.InfiniteTimeSpan);
        }

        _ = MonitorCompletionAsync();
    }

    public ITargetBlock<FlowMessage<ProjectionEvent>> Input => _processor;

    public ISourceBlock<FlowMessage<FlowResult<EventExpectationResult>>> Output => _output;

    public ISourceBlock<FlowEvent> Events => _events;

    public Task Completion => _completion.Task;

    public int ObservedEventCount
    {
        get
        {
            lock (_gate)
            {
                return _observedEvents.Count;
            }
        }
    }

    public void Complete() => _processor.Complete();

    public async Task CompleteWithResultAsync()
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
        lock (_gate)
        {
            if (_resolved)
                return;
        }

        EventSummary summary;
        bool matched;
        try
        {
            summary = CreateSummary(message.Payload);
            matched = EventFilterMatcher.IsMatch(message.Payload, _filter);
        }
        catch (Exception exception)
        {
            ResolveEvaluationFailure(message, exception);
            return;
        }

        Resolution? resolution = null;
        lock (_gate)
        {
            if (_resolved)
                return;

            _lastMessage = message;
            RememberObservedEvent(summary);
            if (matched)
            {
                var satisfied = _kind == EventExpectationNodeKind.Expect;
                resolution = ClaimSuccess(
                    message,
                    satisfied ? ExpectationResultKinds.Matched : ExpectationResultKinds.Unmet,
                    satisfied,
                    matched: true,
                    timedOut: false,
                    summary,
                    satisfied ? "Matching event observed." : "Guarded event observed.");
            }
        }

        Publish(resolution);
    }

    private void ResolveOnTimeout()
    {
        Resolution? resolution;
        lock (_gate)
        {
            var satisfied = _kind == EventExpectationNodeKind.Guard;
            resolution = ClaimSuccess(
                _lastMessage,
                ExpectationResultKinds.TimedOut,
                satisfied,
                matched: false,
                timedOut: true,
                matchedEvent: null,
                satisfied
                    ? "Guard timeout completed without a matching event."
                    : "Expected event was not observed before timeout.");
        }

        Publish(resolution);
    }

    private void ResolveOnCompletion()
    {
        Resolution? resolution;
        lock (_gate)
        {
            var satisfied = _kind == EventExpectationNodeKind.Guard;
            resolution = ClaimSuccess(
                _lastMessage,
                ExpectationResultKinds.Completed,
                satisfied,
                matched: false,
                timedOut: false,
                matchedEvent: null,
                satisfied
                    ? "Input completed without a matching event."
                    : "Input completed before a matching event was observed.");
        }

        Publish(resolution);
    }

    private void ResolveEvaluationFailure(
        FlowMessage<ProjectionEvent> message,
        Exception exception)
    {
        Resolution? resolution;
        lock (_gate)
        {
            if (_resolved)
                return;

            _resolved = true;
            _lastMessage = message;
            var timestamp = _clock.GetUtcNow();
            var error = new DataFlowError(
                ExpectationErrorCodeNames.EvaluationFailed,
                $"event.expectation failed to evaluate input: {exception.Message}",
                category: "Expectations",
                isTransient: false,
                details: CreateErrorDetails(message.Payload, exception));
            resolution = new Resolution(
                message.With(FlowResult<EventExpectationResult>.Failure(
                    ExpectationResultKinds.EvaluationFailed,
                    error,
                    timestamp)),
                ExpectationDiagnosticNames.EvaluationFailed,
                FlowEventLevel.Warning,
                error.Message,
                Satisfied: null,
                Matched: null,
                TimedOut: null,
                IsError: true,
                ReleaseTimer());
        }

        Publish(resolution);
    }

    private Resolution? ClaimSuccess(
        FlowMessage<ProjectionEvent>? origin,
        string resultKind,
        bool satisfied,
        bool matched,
        bool timedOut,
        EventSummary? matchedEvent,
        string reason)
    {
        if (_resolved)
            return null;

        _resolved = true;
        var timestamp = _clock.GetUtcNow();
        var result = new EventExpectationResult
        {
            EvaluatedAt = timestamp,
            Name = _options.Name,
            Kind = _kind == EventExpectationNodeKind.Expect
                ? EventExpectationResultKind.Expect
                : EventExpectationResultKind.Guard,
            Satisfied = satisfied,
            Matched = matched,
            TimedOut = timedOut,
            MatchedEvent = matchedEvent,
            ObservedEvents = _observedEvents.ToArray(),
            Filter = CopyFilter(_filter),
            Reason = reason
        };
        var payload = FlowResult<EventExpectationResult>.Success(
            resultKind,
            result,
            timestamp);
        var output = origin is null ? FlowMessage.Create(payload) : origin.With(payload);
        var diagnosticName = matched
            ? ExpectationDiagnosticNames.Matched
            : timedOut
                ? ExpectationDiagnosticNames.TimedOut
                : ExpectationDiagnosticNames.Completed;
        return new Resolution(
            output,
            diagnosticName,
            satisfied ? FlowEventLevel.Information : FlowEventLevel.Warning,
            reason,
            satisfied,
            matched,
            timedOut,
            IsError: false,
            ReleaseTimer());
    }

    private ITimer? ReleaseTimer()
    {
        var timer = _timeoutTimer;
        _timeoutTimer = null;
        return timer;
    }

    private void Publish(Resolution? resolution)
    {
        if (resolution is null)
            return;

        resolution.Timer?.Dispose();
        _output.Post(resolution.Output);
        _events.Post(new FlowEvent
        {
            Timestamp = resolution.Output.Payload.Timestamp,
            CorrelationId = resolution.Output.CorrelationId,
            Name = resolution.DiagnosticName,
            Level = resolution.Level,
            Message = resolution.Message,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = _kind.ToString(),
                ["resultKind"] = resolution.Output.Payload.Kind,
                ["satisfied"] = resolution.Satisfied,
                ["matched"] = resolution.Matched,
                ["timedOut"] = resolution.TimedOut,
                ["observedCount"] = ObservedEventCount,
                ["isError"] = resolution.IsError
            }
        });
    }

    private void RememberObservedEvent(EventSummary summary)
    {
        if (_options.MaxObservedEvents == 0)
            return;

        _observedEvents.Add(summary);
        while (_observedEvents.Count > _options.MaxObservedEvents)
            _observedEvents.RemoveAt(0);
    }

    private async Task MonitorCompletionAsync()
    {
        try
        {
            await _processor.Completion.ConfigureAwait(false);
            ResolveOnCompletion();
            _output.Complete();
            await _output.Completion.ConfigureAwait(false);
            _events.Complete();
            await _events.Completion.ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            ITimer? timer;
            lock (_gate)
            {
                timer = ReleaseTimer();
            }

            timer?.Dispose();
            ((IDataflowBlock)_output).Fault(exception);
            _events.Complete();
            _completion.TrySetException(exception);
        }
    }

    private static EventExpectationOptions ResolveOptions(EventExpectationOptions? options)
    {
        var resolved = options ?? new EventExpectationOptions();
        if (resolved.TimeoutMilliseconds.HasValue && resolved.TimeoutMilliseconds.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event expectation option 'timeoutMilliseconds' must be greater than zero when set.");
        }
        if (resolved.MaxObservedEvents < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event expectation option 'maxObservedEvents' must be zero or greater.");
        }
        if (resolved.MaxPreviewChars < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event expectation option 'maxPreviewChars' must be zero or greater.");
        }
        if (resolved.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event expectation option 'boundedCapacity' must be greater than zero.");
        }

        return resolved with
        {
            Filter = CopyFilter(resolved.Filter ?? new EventFilter())
        };
    }

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

    private string? Truncate(string? value)
    {
        if (value is null || _options.MaxPreviewChars <= 0)
            return null;

        return value.Length <= _options.MaxPreviewChars
            ? value
            : value[.._options.MaxPreviewChars];
    }

    private static EventFilter CopyFilter(EventFilter filter)
        => filter with { Attributes = CopyDictionary(filter.Attributes) };

    private static Dictionary<string, string> CopyDictionary(
        IReadOnlyDictionary<string, string>? source)
        => source is null
            ? []
            : new Dictionary<string, string>(source, StringComparer.Ordinal);

    private static FlowValue CreateErrorDetails(
        ProjectionEvent? flowEvent,
        Exception exception)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name)
        };
        if (flowEvent is not null)
        {
            details["type"] = OptionalValue(flowEvent.Type);
            details["source"] = OptionalValue(flowEvent.Source);
            details["subject"] = OptionalValue(flowEvent.Subject);
            details["status"] = OptionalValue(flowEvent.Status);
            details["channel"] = OptionalValue(flowEvent.Channel);
        }

        return FlowValue.FromObject(details);
    }

    private static FlowValue OptionalValue(string? value)
        => value is null ? FlowValue.Null : FlowValue.From(value);

    private sealed record Resolution(
        FlowMessage<FlowResult<EventExpectationResult>> Output,
        string DiagnosticName,
        FlowEventLevel Level,
        string Message,
        bool? Satisfied,
        bool? Matched,
        bool? TimedOut,
        bool IsError,
        ITimer? Timer);
}
