using System.Text.Json;
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
/// output. Rule outcomes, timeout, and input
/// completion are successful variants; expected evaluation failures are error
/// variants on the same output.
/// </summary>
public sealed class EventExpectationNode
    : FlowNode<ProjectionEvent, EventExpectationResult>
{
    private readonly object _gate = new();
    private readonly EventExpectationOptions _options;
    private readonly EventFilter _filter;
    private readonly TimeProvider _clock;
    private readonly EventExpectationNodeKind _kind;
    private readonly List<EventSummary> _observedEvents = [];
    private readonly ActionBlock<Resolution> _timerResolutions;
    private ITimer? _timeoutTimer;
    private FlowMessage<ProjectionEvent>? _lastMessage;
    private bool _resolved;
    private bool _acceptTimerResolutions = true;

    public EventExpectationNode(
        EventExpectationOptions? options = null,
        TimeProvider? clock = null)
        : this(new ValidatedOptions(ResolveOptions(options)), clock)
    {
    }

    private EventExpectationNode(
        ValidatedOptions options,
        TimeProvider? clock)
        : base(options.FlowNodeOptions)
    {
        _options = options.ExpectationOptions;
        _filter = _options.Filter!;
        _clock = clock ?? TimeProvider.System;
        _kind = _options.Kind;
        _timerResolutions = new ActionBlock<Resolution>(
            PublishAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _options.BoundedCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });

        if (_options.TimeoutMilliseconds is { } milliseconds)
        {
            _timeoutTimer = _clock.CreateTimer(
                static state => ((EventExpectationNode)state!).ResolveOnTimeout(),
                this,
                TimeSpan.FromMilliseconds(milliseconds),
                Timeout.InfiniteTimeSpan);
        }
    }

    protected override bool HandlesErrors => true;

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

    public async Task CompleteWithResultAsync()
    {
        Complete();
        await Completion.ConfigureAwait(false);
    }

    protected override async Task ProcessAsync(FlowMessage<ProjectionEvent> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Resolution? resolution = null;
        if (message.IsError)
        {
            lock (_gate)
            {
                if (_resolved)
                    return;
                _resolved = true;
                _lastMessage = message;
                resolution = new Resolution(
                    message.WithError<EventExpectationResult>(message.Error!),
                    ExpectationDiagnosticNames.EvaluationFailed,
                    FlowEventLevel.Warning,
                    message.Error!.Message,
                    ExpectationResultKinds.EvaluationFailed,
                    Satisfied: null,
                    Matched: null,
                    TimedOut: null,
                    IsError: true,
                    ReleaseTimer());
            }

            await PublishAsync(resolution).ConfigureAwait(false);
            return;
        }

        lock (_gate)
        {
            if (_resolved)
                return;
        }

        EventSummary summary;
        bool matched;
        try
        {
            summary = CreateSummary(message.Value);
            matched = EventFilterMatcher.IsMatch(message.Value, _filter);
        }
        catch (Exception exception)
        {
            resolution = ClaimEvaluationFailure(message, exception);
            if (resolution is not null)
                await PublishAsync(resolution).ConfigureAwait(false);
            return;
        }

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

        if (resolution is not null)
            await PublishAsync(resolution).ConfigureAwait(false);
    }

    private void ResolveOnTimeout()
    {
        Resolution? resolution;
        var accepted = true;
        lock (_gate)
        {
            if (!_acceptTimerResolutions)
                return;

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
            if (resolution is not null)
                accepted = _timerResolutions.Post(resolution);
        }

        if (!accepted)
        {
            resolution!.Timer?.Dispose();
            Fault(new InvalidOperationException(
                "event expectation timeout emission capacity was exhausted."));
        }
    }

    protected override async ValueTask OnInputCompletedAsync()
    {
        Resolution? resolution;
        lock (_gate)
        {
            _acceptTimerResolutions = false;
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


        _timerResolutions.Complete();
        await _timerResolutions.Completion.ConfigureAwait(false);
        if (resolution is not null)
            await PublishAsync(resolution).ConfigureAwait(false);
    }

    protected override async ValueTask OnDisposeAsync()
    {
        ITimer? timer;
        lock (_gate)
        {
            _acceptTimerResolutions = false;
            timer = ReleaseTimer();
        }

        timer?.Dispose();
        _timerResolutions.Complete();
        try
        {
            await _timerResolutions.Completion.ConfigureAwait(false);
        }
        catch
        {
            // Node completion remains the authoritative fault surface.
        }
    }

    private Resolution? ClaimEvaluationFailure(
        FlowMessage<ProjectionEvent> message,
        Exception exception)
    {
        lock (_gate)
        {
            if (_resolved)
                return null;

            _resolved = true;
            _lastMessage = message;
            var timestamp = _clock.GetUtcNow();
            var error = new DataFlowError(
                ExpectationErrorCodeNames.EvaluationFailed,
                $"event.expect failed to evaluate input: {exception.Message}",
                category: "Expectations",
                isTransient: false,
                details: CreateErrorDetails(message.Value, exception));
            return new Resolution(
                message.WithError<EventExpectationResult>(error),
                ExpectationDiagnosticNames.EvaluationFailed,
                FlowEventLevel.Warning,
                error.Message,
                ExpectationResultKinds.EvaluationFailed,
                Satisfied: null,
                Matched: null,
                TimedOut: null,
                IsError: true,
                ReleaseTimer());
        }
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
            Filter = _filter,
            Reason = reason
        };
        var output = origin is null ? FlowMessage.Create(result) : origin.With(result);
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
            resultKind,
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

    private async Task PublishAsync(Resolution resolution)
    {
        resolution.Timer?.Dispose();
        await EmitAsync(resolution.Output, Stopping).ConfigureAwait(false);
        EmitEvent(new FlowEvent
        {
            Timestamp = resolution.Output.Timestamp,
            CorrelationId = resolution.Output.CorrelationId,
            Name = resolution.DiagnosticName,
            Level = resolution.Level,
            Message = resolution.Message,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = _kind.ToString(),
                ["resultKind"] = resolution.ResultKind,
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
            Filter = resolved.Filter ?? new EventFilter()
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
            Attributes = flowEvent.Attributes
        };

    private string? Truncate(string? value)
    {
        if (value is null || _options.MaxPreviewChars <= 0)
            return null;

        return value.Length <= _options.MaxPreviewChars
            ? value
            : value[.._options.MaxPreviewChars];
    }

    private static JsonElement CreateErrorDetails(
        ProjectionEvent? flowEvent,
        Exception exception)
    {
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name
        };
        if (flowEvent is not null)
        {
            details["type"] = flowEvent.Type;
            details["source"] = flowEvent.Source;
            details["subject"] = flowEvent.Subject;
            details["status"] = flowEvent.Status;
            details["channel"] = flowEvent.Channel;
        }

        return JsonSerializer.SerializeToElement(details);
    }

    private sealed record Resolution(
        FlowMessage<EventExpectationResult> Output,
        string DiagnosticName,
        FlowEventLevel Level,
        string Message,
        string ResultKind,
        bool? Satisfied,
        bool? Matched,
        bool? TimedOut,
        bool IsError,
        ITimer? Timer);

    private sealed class ValidatedOptions(EventExpectationOptions expectationOptions)
    {
        public EventExpectationOptions ExpectationOptions { get; } = expectationOptions;

        public FlowNodeOptions FlowNodeOptions { get; } = new()
        {
            InputCapacity = expectationOptions.BoundedCapacity,
            OutputCapacity = expectationOptions.BoundedCapacity
        };
    }
}
