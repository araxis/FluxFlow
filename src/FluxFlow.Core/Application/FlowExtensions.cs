using FluxFlow.Core.Execution;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Core.Application;

/// <summary>Application composition using registered native blocks and full port addresses.</summary>
public static class FlowExtensions
{
    /// <summary>Declares a link between existing full port addresses during Build.
    /// Start connects the route, preserving fanout, delivery policy and coordinated completion.</summary>
    public static FlowLink Link(this IServiceProvider ports, string source, string target,
        Predicate<FlowMessage>? when = null, FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        Action<FlowMessage, Exception>? onRejected = null)
        => AddLink(ports, source, target, when, delivery, false, onRejected);

    /// <summary>Declares the route used when no ordinary link matches the message.</summary>
    public static FlowLink LinkFallback(this IServiceProvider ports, string source, string target,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable,
        Action<FlowMessage, Exception>? onRejected = null)
        => AddLink(ports, source, target, null, delivery, true, onRejected);

    private static FlowLink AddLink(IServiceProvider ports, string source, string target,
        Predicate<FlowMessage>? condition, FlowDeliveryPolicy delivery, bool fallback,
        Action<FlowMessage, Exception>? onRejected)
    {
        var output = FindPort<ISourceBlock<FlowMessage>>(ports, source, "Output");
        var input = FindPort<ITargetBlock<FlowMessage>>(ports, target, "Input");
        var link = new FlowLink(source, output, target, input, condition, delivery, fallback, onRejected);
        FindPort<Action<FlowLink>>(ports, source, "Output")(link);
        return link;
    }

    private static T FindPort<T>(IServiceProvider ports, string address, string direction) where T : class
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(address);
        return ports.GetKeyedService<T>(address)
            ?? throw new KeyNotFoundException($"{direction} '{address}' was not found.");
    }

    /// <summary>Finds the actual component instance by its workflow.component address.</summary>
    public static IFlowNode GetComponent(this FlowApplication application, string address)
    {
        ArgumentNullException.ThrowIfNull(application);
        return FindPort<IFlowNode>(application.Services ?? throw new InvalidOperationException("Application has not been composed."), address, "Component");
    }

}
