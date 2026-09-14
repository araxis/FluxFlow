using FluxFlow.Core;
using FluxFlow.Json.Links;
using FluxFlow.Composition.Links;

namespace FluxFlow.Composition.Definition;

/// <summary>
/// Declares links at their input endpoint, matching input-oriented document definition.
/// Calls return the same input handle so multiple sources can be listed flatly.
/// </summary>
/// <remarks>
/// Connections use the existing application graph and validation. Endpoints must
/// belong to the same application; cross-workflow links are allowed. Fan-in means
/// multiple upstream sources, not a join or an ordering guarantee. Declare each
/// link at only one endpoint, using either LinkFrom or LinkTo.
/// </remarks>
public static class InputPortConnectionExtensions
{
    /// <summary>Declares this input as the output's fallback target.</summary>
    public static InputPortHandle<T> OtherwiseFrom<T>(this InputPortHandle<T> target,
        OutputPortHandle<T> source, FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        source.Workflow.AddFallback(source, target, ApplicationLinkDeclarationSide.Input, delivery);
        return target;
    }

    /// <summary>Declares this signal input as the output's fallback target.</summary>
    public static SignalInputPortHandle OtherwiseFrom<T>(this SignalInputPortHandle target,
        OutputPortHandle<T> source, FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        source.Workflow.AddFallback(source, target, ApplicationLinkDeclarationSide.Input, delivery);
        return target;
    }

    /// <summary>Connect a matching typed output to this input.</summary>
    public static InputPortHandle<T> LinkFrom<T>(
        this InputPortHandle<T> target,
        OutputPortHandle<T> source,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        source.Workflow.AddConnection(source, target, allowCrossWorkflow: true,
            declarationSide: ApplicationLinkDeclarationSide.Input, delivery: delivery);
        return target;
    }

    /// <summary>Connect using an expression condition in the configured expression language.</summary>
    public static InputPortHandle<T> LinkFrom<T>(
        this InputPortHandle<T> target,
        OutputPortHandle<T> source,
        string condition,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        source.Workflow.AddConnection(source, target, condition, allowCrossWorkflow: true,
            declarationSide: ApplicationLinkDeclarationSide.Input, delivery: delivery);
        return target;
    }

    /// <summary>Connect using a code-only predicate evaluated on successful payloads.</summary>
    public static InputPortHandle<T> LinkFrom<T>(
        this InputPortHandle<T> target,
        OutputPortHandle<T> source,
        Func<T, bool> when,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        source.Workflow.AddConnection(source, target, when, allowCrossWorkflow: true,
            declarationSide: ApplicationLinkDeclarationSide.Input, delivery: delivery);
        return target;
    }

    /// <summary>Connect a typed output to this payload-independent signal input.</summary>
    public static SignalInputPortHandle LinkFrom<T>(
        this SignalInputPortHandle target,
        OutputPortHandle<T> source,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        source.Workflow.AddConnection(source, target, allowCrossWorkflow: true,
            declarationSide: ApplicationLinkDeclarationSide.Input, delivery: delivery);
        return target;
    }

    /// <summary>Connect a signal input using an expression condition.</summary>
    public static SignalInputPortHandle LinkFrom<T>(
        this SignalInputPortHandle target,
        OutputPortHandle<T> source,
        string condition,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        source.Workflow.AddConnection(source, target, condition, allowCrossWorkflow: true,
            declarationSide: ApplicationLinkDeclarationSide.Input, delivery: delivery);
        return target;
    }

    /// <summary>Connect a signal input using a code-only successful-payload predicate.</summary>
    public static SignalInputPortHandle LinkFrom<T>(
        this SignalInputPortHandle target,
        OutputPortHandle<T> source,
        Func<T, bool> when,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        source.Workflow.AddConnection(source, target, when, allowCrossWorkflow: true,
            declarationSide: ApplicationLinkDeclarationSide.Input, delivery: delivery);
        return target;
    }
}
