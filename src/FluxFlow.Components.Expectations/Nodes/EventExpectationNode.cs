using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Diagnostics;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Expectations.Nodes;

/// <summary>
/// A standalone event-expectation node. Post a <c>FlowMessage&lt;ProjectionEvent&gt;</c>
/// to <c>Input</c>; the node watches for an event that matches its configured
/// <see cref="EventFilter"/> and resolves exactly once into a single
/// <c>FlowMessage&lt;EventExpectationResult&gt;</c> broadcast on <c>Output</c>
/// (failures on <c>Errors</c>, diagnostics on <c>Events</c>). An
/// <see cref="EventExpectationNodeKind.Expect"/> node is satisfied when a matching
/// event arrives; a <see cref="EventExpectationNodeKind.Guard"/> node is satisfied
/// when none arrives. The node resolves on the first of three triggers: a matching
/// event, a configured timeout (armed over the injected <see cref="TimeProvider"/>),
/// or input completion via <see cref="CompleteWithResultAsync"/>. Works with nothing
/// but <c>new EventExpectationNode(options)</c> — no engine. Events are processed
/// strictly in order on a single worker so resolution stays consistent.
/// </summary>
public sealed class EventExpectationNode : FlowNode<ProjectionEvent, EventExpectationResult>
{
    // Reference-identity sentinel posted through the ordered input pump to resolve the
    // expectation once every real event ahead of it has been folded in. Carrying the
    // completion-resolution in-band keeps it ordered behind every observed event.
    private static readonly ProjectionEvent CompletionSentinel = new()
    {
        Timestamp = default,
        Type = "__complete__",
        Source = "__complete__"
    };

    private readonly object _stateLock = new();
    private readonly EventExpectationOptions _options;
    private readonly EventFilter _filter;
    private readonly TimeProvider _clock;
    private readonly EventExpectationNodeKind _kind;
    private readonly TimeSpan? _timeout;
    private readonly List<EventSummary> _observedEvents = [];
    private ITimer? _timeoutTimer;
    private CorrelationId? _lastCorrelationId;
    private bool _resolved;

    public EventExpectationNode(
        EventExpectationOptions? options = null,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = ResolveOptions(options).BoundedCapacity,
            MaxDegreeOfParallelism = 1
        })
    {
        _options = ResolveOptions(options);
        // A null filter means match-all (the matcher and the result copy both expect a value).
        _filter = _options.Filter ?? new EventFilter();
        _clock = clock ?? TimeProvider.System;
        _kind = _options.Kind;

        _timeout = _options.TimeoutMilliseconds.HasValue
            ? TimeSpan.FromMilliseconds(_options.TimeoutMilliseconds.Value)
            : null;

        // Arm the timeout over the injected TimeProvider so a host (or a test with a
        // FakeTimeProvider) drives it deterministically — no real-time wait.
        if (_timeout is { } timeout)
        {
            _timeoutTimer = _clock.CreateTimer(
                static state => ((EventExpectationNode)state!).OnTimeout(),
                this,
                timeout,
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Number of events observed so far (matching or not), capped at the configured max.</summary>
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

    /// <summary>
    /// Resolves the expectation against input completion, emitting the
    /// not-matched/completed result first when no earlier trigger fired. The
    /// resolution is sent through the ordered input pump, so it lands behind every
    /// event already posted before the node completes.
    /// </summary>
    public async Task CompleteWithResultAsync()
    {
        await Input.SendAsync(FlowMessage.Create(CompletionSentinel)).ConfigureAwait(false);
        Complete();
    }

    protected override Task ProcessAsync(FlowMessage<ProjectionEvent> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (ReferenceEquals(message.Payload, CompletionSentinel))
        {
            ResolveOnCompletion();
            return Task.CompletedTask;
        }

        var flowEvent = message.Payload;
        try
        {
            _lastCorrelationId = message.CorrelationId;
            var summary = CreateSummary(flowEvent);
            RememberObservedEvent(summary);

            if (!EventFilterMatcher.IsMatch(flowEvent, _filter))
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
                    reason: "Matching event observed.",
                    correlationId: message.CorrelationId);
            }
            else
            {
                Resolve(
                    satisfied: false,
                    matched: true,
                    timedOut: false,
                    matchedEvent: summary,
                    reason: "Guarded event observed.",
                    correlationId: message.CorrelationId);
            }
        }
        catch (Exception exception)
        {
            EmitError(new FlowError
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Code = ExpectationsErrorCodes.EvaluationFailed,
                Message = $"event expectation failed: {exception.Message}",
                Context = CreateEventContext(flowEvent),
                Exception = exception
            });
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = ExpectationDiagnosticNames.EvaluationFailed,
                Level = FlowEventLevel.Error,
                Message = "event expectation failed.",
                Attributes = CreateEventAttributes(flowEvent)
            });
        }

        return Task.CompletedTask;
    }

    protected override ValueTask OnDisposeAsync()
    {
        // The pump has drained and the output port is closed by the base node, so a
        // pending expectation can no longer emit. Resolution before completion is the
        // caller's job via CompleteWithResultAsync; here we only tear the timer down.
        _timeoutTimer?.Dispose();
        _timeoutTimer = null;
        return ValueTask.CompletedTask;
    }

    private void OnTimeout()
    {
        if (_kind == EventExpectationNodeKind.Expect)
        {
            Resolve(
                satisfied: false,
                matched: false,
                timedOut: true,
                matchedEvent: null,
                reason: "Expected event was not observed before timeout.",
                correlationId: _lastCorrelationId);
        }
        else
        {
            Resolve(
                satisfied: true,
                matched: false,
                timedOut: true,
                matchedEvent: null,
                reason: "Guard timeout completed without a matching event.",
                correlationId: _lastCorrelationId);
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
                reason: "Input completed before a matching event was observed.",
                correlationId: _lastCorrelationId);
        }
        else
        {
            Resolve(
                satisfied: true,
                matched: false,
                timedOut: false,
                matchedEvent: null,
                reason: "Input completed without a matching event.",
                correlationId: _lastCorrelationId);
        }
    }

    private bool Resolve(
        bool satisfied,
        bool matched,
        bool timedOut,
        EventSummary? matchedEvent,
        string reason,
        CorrelationId? correlationId)
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
        }

        // The matched event drives the result's correlation id when there was a match;
        // otherwise it rides the last observed event's id, or is a fresh exchange.
        var message = correlationId is { } id
            ? new FlowMessage<EventExpectationResult>(id, result)
            : FlowMessage.Create(result);
        EmitResult(message);
        EmitResultDiagnostic(result);
        return true;
    }

    private void EmitResult(FlowMessage<EventExpectationResult> message)
    {
        if (Emit(message))
        {
            return;
        }

        var text = "event expectation result output was full; result was dropped.";
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Code = ExpectationsErrorCodes.ResultDropped,
            Message = text,
            Context = CreateResultContext(message.Payload)
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = ExpectationDiagnosticNames.ResultDropped,
            Level = FlowEventLevel.Warning,
            Message = text,
            Attributes = CreateResultAttributes(message.Payload)
        });
    }

    private void EmitResultDiagnostic(EventExpectationResult result)
    {
        var name = result.Matched
            ? ExpectationDiagnosticNames.Matched
            : result.TimedOut
                ? ExpectationDiagnosticNames.TimedOut
                : ExpectationDiagnosticNames.Completed;
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = _lastCorrelationId,
            Name = name,
            Level = result.Satisfied ? FlowEventLevel.Information : FlowEventLevel.Warning,
            Message = result.Reason,
            Attributes = CreateResultAttributes(result)
        });
    }

    private void RememberObservedEvent(EventSummary summary)
    {
        if (_options.MaxObservedEvents == 0)
        {
            return;
        }

        lock (_stateLock)
        {
            _observedEvents.Add(summary);
            while (_observedEvents.Count > _options.MaxObservedEvents)
            {
                _observedEvents.RemoveAt(0);
            }
        }
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
        {
            return null;
        }

        return value.Length <= _options.MaxPreviewChars
            ? value
            : value[.._options.MaxPreviewChars];
    }

    private static EventExpectationOptions ResolveOptions(EventExpectationOptions? options)
    {
        var resolved = options ?? new EventExpectationOptions();
        Validate(resolved);
        return resolved;
    }

    private static void Validate(EventExpectationOptions options)
    {
        if (options.TimeoutMilliseconds.HasValue && options.TimeoutMilliseconds.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event expectation option 'timeoutMilliseconds' must be greater than zero when set.");
        }

        if (options.MaxObservedEvents < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event expectation option 'maxObservedEvents' must be zero or greater.");
        }

        if (options.MaxPreviewChars < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event expectation option 'maxPreviewChars' must be zero or greater.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "event expectation option 'boundedCapacity' must be greater than zero.");
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

    private static Dictionary<string, object?> CreateEventAttributes(ProjectionEvent? flowEvent)
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
}
