using FluxFlow.Core.Messaging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FluxFlow.Core.Nodes;

/// <summary>In-process diagnostics. Hosts own subscriptions and logging/export policy.</summary>
public static class FlowDiagnostics
{
    public const string ComponentAddressTag = "flow.component.address";
    private static readonly ConditionalWeakTable<object, string> ComponentAddresses = new();

    /// <summary>Attaches a host-assigned diagnostic address once, before processing starts.
    /// Does not retain the component or interpret address syntax. A second attachment rejects
    /// the same instance, including under another address or provider.</summary>
    public static void AttachComponentAddress(object component, string address)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        try { ComponentAddresses.Add(component, address); }
        catch (ArgumentException error)
        {
            throw new ArgumentException("The same component instance cannot be registered twice.", nameof(component), error);
        }
    }

    internal static string? GetComponentAddress(object component)
        => ComponentAddresses.TryGetValue(component, out var address) ? address : null;

    internal static TagList ComponentTags(object component)
    {
        var tags = new TagList { { "flow.component.type", component.GetType().FullName } };
        if (GetComponentAddress(component) is { } address) tags.Add(ComponentAddressTag, address);
        return tags;
    }

    public const string ListenerName = "FluxFlow.Core";
    public const string ProcessingExceptionEvent = "FluxFlow.Processing.Exception";
    public const string LifecycleExceptionEvent = "FluxFlow.Lifecycle.Exception";

    /// <summary>
    /// Reports a lifecycle failure without a workflow message or inherited Activity.
    /// Call at the owning failure boundary, not at every catch that rethrows to it.
    /// </summary>
    public static void LifecycleException(object component, string operation, Exception exception)
    {
        try
        {
            using var context = FlowTracing.SuppressContext();
            FlowRuntimeMetrics.Exception(component, operation, exception);
            var listener = Source.Instance;
            if (!listener.IsEnabled(LifecycleExceptionEvent)) return;
            listener.Write(LifecycleExceptionEvent, new FlowLifecycleException(
                exception, component.GetType().FullName ?? component.GetType().Name, operation)
            { ComponentAddress = GetComponentAddress(component) });
        }
        catch
        {
            // Observers must not change lifecycle failure or completion.
        }
    }

    /// <summary>
    /// Reports a caught processing exception before converting it to a domain FlowError.
    /// Marks the current processing Activity failed. Call once at the handling boundary;
    /// exceptions rethrown to the node boundary are reported there instead.
    /// </summary>
    public static void ProcessingException(FlowMessage message, object component, Exception exception)
    {
        FlowRuntimeMetrics.Exception(component, "processing", exception);
        try
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
            if (Activity.Current?.IsAllDataRequested == true)
            {
                Activity.Current.SetTag("error.type", exception.GetType().FullName);
                Activity.Current.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                {
                    { "exception.type", exception.GetType().FullName },
                    { "exception.message", exception.Message },
                    { "exception.stacktrace", exception.ToString() }
                }));
            }
            var listener = Source.Instance;
            if (!listener.IsEnabled(ProcessingExceptionEvent)) return;
            listener.Write(ProcessingExceptionEvent, new FlowProcessingException(
                exception, component.GetType().FullName ?? component.GetType().Name,
                message.MessageId, message.CorrelationId, message.TraceId, Activity.Current)
            { ComponentAddress = GetComponentAddress(component) });
        }
        catch
        {
            // Diagnostic observers must not replace the processing error or fault the node.
        }
    }

    private static class Source
    {
        internal static readonly DiagnosticListener Instance = new(ListenerName);
    }
}

/// <summary>
/// Synchronous, in-process failure observation. Exception is the original object;
/// Activity can be null when tracing is disabled or unsampled. No workflow payload is included.
/// </summary>
public sealed record FlowProcessingException(
    Exception Exception,
    string ComponentType,
    MessageId MessageId,
    CorrelationId? CorrelationId,
    TraceId TraceId,
    Activity? Activity)
{
    /// <summary>Host-assigned component address, when attached; independent of trace sampling.</summary>
    public string? ComponentAddress { get; init; }
}

/// <summary>In-process lifecycle failure; no workflow payload, message IDs or implicit trace parent.</summary>
public sealed record FlowLifecycleException(Exception Exception, string ComponentType, string Operation)
{
    /// <summary>Host-assigned component address, when attached; independent of trace sampling.</summary>
    public string? ComponentAddress { get; init; }
}
