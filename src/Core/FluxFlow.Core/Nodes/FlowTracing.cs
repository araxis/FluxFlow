using System.Collections.Immutable;
using System.Diagnostics;

namespace FluxFlow.Nodes;

/// <summary>Optional processing scopes for canonical component implementations. Hosts own listeners and export.</summary>
public static class FlowTracing
{
    private const string TraceParent = "traceparent";
    private const string TraceState = "tracestate";

    internal static IReadOnlyDictionary<string, string>? CaptureHeaders(
        IReadOnlyDictionary<string, string>? headers, bool replaceContext = false)
    {
        if (!replaceContext && headers?.ContainsKey(TraceParent) == true) return headers;
        var current = Activity.Current;
        if (current?.IdFormat != ActivityIdFormat.W3C || current.Id is null) return headers;

        return WithContext(headers, current);
    }

    private static IReadOnlyDictionary<string, string> WithContext(
        IReadOnlyDictionary<string, string>? headers, Activity current)
    {
        var captured = ((ImmutableDictionary<string, string>)FlowMessage.CopyHeaders(headers))
            .SetItem(TraceParent, current.Id!);
        return string.IsNullOrEmpty(current.TraceStateString)
            ? captured.Remove(TraceState)
            : captured.SetItem(TraceState, current.TraceStateString);
    }

    /// <summary>Starts queued processing with the message's context, never the worker's ambient parent.</summary>
    public static OperationActivity StartProcessing(FlowMessage message, object component, string? controlPort = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(component);
        return StartProcessingCore([message], component, controlPort, aggregate: false);
    }

    /// <summary>Starts one aggregate operation with links to its inputs and no arbitrarily selected parent.</summary>
    public static OperationActivity StartProcessing(ReadOnlySpan<FlowMessage> messages, object component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (messages.IsEmpty) throw new ArgumentException("At least one input is required.", nameof(messages));
        foreach (var message in messages) ArgumentNullException.ThrowIfNull(message);
        return StartProcessingCore(messages, component, null, aggregate: true);
    }

    /// <summary>Clears inherited worker context for non-message commands and restores it when disposed.</summary>
    public static OperationActivity SuppressContext()
    {
        var previous = Activity.Current;
        if (previous is null) return default;
        RestoreCurrent(null);
        return new OperationActivity(null, previous);
    }

    private static OperationActivity StartProcessingCore(ReadOnlySpan<FlowMessage> messages,
        object component, string? controlPort, bool aggregate)
    {
        var measurement = FlowRuntimeMetrics.Start(component);
        var previous = Activity.Current;
        Activity? activity = null;
        try
        {
            var source = CoreSource.Instance;
            if (!source.HasListeners())
            {
                Activity.Current = null;
                return new OperationActivity(null, previous, measurement: measurement);
            }

            // A block's captured execution context is not the queued message's parent.
            Activity.Current = null;
            var message = messages[0];
            var context = aggregate ? default : Context(message);
            activity = source.CreateActivity(controlPort is null ? "flow.process" : "flow.control", ActivityKind.Consumer, context,
                links: aggregate ? Links(messages) : context == default ? null : [new ActivityLink(context)],
                idFormat: ActivityIdFormat.W3C);
            activity?.Start();
            if (activity?.IsAllDataRequested == true)
            {
                activity.SetTag("flow.component.type", component.GetType().FullName);
                if (aggregate) activity.SetTag("flow.message.count", messages.Length);
                else
                {
                    activity.SetTag("flow.message_id", message.MessageId.Value);
                    activity.SetTag("flow.trace_id", message.TraceId.Value);
                    activity.SetTag("flow.message.is_error", message.IsError);
                }
                if (controlPort is not null) activity.SetTag("flow.control.port", controlPort);
            }
        }
        catch
        {
            Stop(activity);
            RestoreCurrent(null);
            activity = null;
        }
        return new OperationActivity(activity, previous, measurement: measurement);
    }

    private static ActivityContext Context(FlowMessage message)
    {
        message.Headers.TryGetValue(TraceParent, out var parent);
        message.Headers.TryGetValue(TraceState, out var state);
        ActivityContext.TryParse(parent, state, out var context);
        return context;
    }

    private static List<ActivityLink> Links(ReadOnlySpan<FlowMessage> messages)
    {
        var links = new List<ActivityLink>(messages.Length);
        foreach (var message in messages)
        {
            var context = Context(message);
            if (context != default) links.Add(new ActivityLink(context));
        }
        return links;
    }

    internal static OperationActivity StartRequest(FlowMessage message, string replyAddress)
    {
        var previous = Activity.Current;
        Activity? activity = null;
        try
        {
            var source = CoreSource.Instance;
            if (!source.HasListeners()) return default;
            message.Headers.TryGetValue(TraceParent, out var parent);
            message.Headers.TryGetValue(TraceState, out var state);
            ActivityContext.TryParse(parent, state, out var creation);
            var context = previous?.IdFormat == ActivityIdFormat.W3C ? previous.Context : creation;
            Activity.Current = null;
            activity = source.CreateActivity("flow.request", ActivityKind.Internal, context,
                links: creation == default ? null : [new ActivityLink(creation)], idFormat: ActivityIdFormat.W3C);
            activity?.Start();
            if (activity?.IsAllDataRequested == true)
            {
                activity.SetTag("flow.reply.address", replyAddress);
                activity.SetTag("flow.message_id", message.MessageId.Value);
                activity.SetTag("flow.trace_id", message.TraceId.Value);
                activity.SetTag("flow.message.is_error", message.IsError);
            }
        }
        catch
        {
            Stop(activity);
            activity = null;
        }
        if (activity is null) RestoreCurrent(previous);
        return new OperationActivity(activity, previous, "flow.request.outcome");
    }

    private static void Stop(Activity? activity)
    {
        try { activity?.Dispose(); }
        catch { /* Host activity callbacks cannot change component outcomes. */ }
    }

    private static void RestoreCurrent(Activity? activity)
    {
        // A captured execution context can outlive the activity it carries.
        try { Activity.Current = activity?.IsStopped == true ? null : activity; }
        catch { /* The ambient value is set before current-change callbacks execute. */ }
    }

    // Message creation does not initialize a tracing source or invoke host listeners.
    private static class CoreSource
    {
        internal static readonly ActivitySource Instance = new("FluxFlow.Core");
    }

    /// <summary>A synchronous scope that must be disposed in the same logical async operation where it started.</summary>
    public readonly struct OperationActivity : IDisposable
    {
        private readonly Activity? activity;
        private readonly Activity? previous;
        private readonly string? outcomeTag;
        private readonly bool _restore;
        private readonly FlowRuntimeMetrics.Measurement _measurement;

        internal OperationActivity(Activity? activity, Activity? previous, string outcomeTag = "flow.processing.outcome",
            FlowRuntimeMetrics.Measurement measurement = default)
        {
            this.activity = activity;
            this.previous = previous;
            this.outcomeTag = outcomeTag;
            _restore = true;
            _measurement = measurement;
        }

        /// <summary>Records failure type without exception text or message payload.</summary>
        public void Failed(Exception error)
        {
            ArgumentNullException.ThrowIfNull(error);
            activity?.SetStatus(ActivityStatusCode.Error);
            if (activity?.IsAllDataRequested == true)
                activity.SetTag("error.type", error.GetType().FullName);
        }

        /// <summary>Records an operation outcome when full-data sampling is enabled.</summary>
        public void Outcome(string outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if (activity?.IsAllDataRequested == true)
                activity.SetTag(outcomeTag!, outcome);
        }

        /// <summary>Records cancellation without marking it as an operation failure.</summary>
        public void Canceled() => Outcome("canceled");

        internal IReadOnlyDictionary<string, string> CaptureHeaders(IReadOnlyDictionary<string, string> headers)
            => activity?.Id is null ? headers : WithContext(headers, activity);

        void IDisposable.Dispose()
        {
            _measurement.Stop();
            if (!_restore) return;
            Stop(activity);
            RestoreCurrent(previous);
        }
    }
}
