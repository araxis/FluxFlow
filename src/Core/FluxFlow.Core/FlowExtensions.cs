using FluxFlow.Nodes;

namespace FluxFlow.Core;

/// <summary>Convenient construction syntax over the existing application and endpoints.</summary>
public static class FlowExtensions
{
    /// <summary>Adds and configures a workflow immediately, returning the same application.
    /// If configuration throws, successful additions remain owned by the application.</summary>
    public static FlowApplication Workflow(this FlowApplication application, string name, Action<FlowWorkflow> configure)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(configure);
        configure(application.AddWorkflow(name));
        return application;
    }

    /// <summary>Links to a target and returns this output for further fanout connections.</summary>
    public static FlowOutputEndpoint To(this FlowOutputEndpoint output, FlowInputEndpoint input,
        Predicate<FlowMessage>? when = null, FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        Action<FlowMessage, Exception>? onRejected = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.LinkTo(input, when, delivery, onRejected);
        return output;
    }

    /// <summary>Links from a source and returns this input for further fan-in connections.</summary>
    public static FlowInputEndpoint From(this FlowInputEndpoint input, FlowOutputEndpoint output,
        Predicate<FlowMessage>? when = null, FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        Action<FlowMessage, Exception>? onRejected = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        input.LinkFrom(output, when, delivery, onRejected);
        return input;
    }
}
