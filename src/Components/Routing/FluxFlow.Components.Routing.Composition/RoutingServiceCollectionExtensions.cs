using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingServiceCollectionExtensions
{
    public static IServiceCollection AddRouting(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(RoutingComponentDefinition.Types.Window, CreateJsonWindowNode)
            .AddComponent(RoutingComponentDefinition.Types.Correlation, CreateJsonCorrelationNode)
            .AddComponent(RoutingComponentDefinition.Types.Join, CreateJsonJoinNode)
            .AddComponent("flow.gate", static context =>
            {
                var options = context.BindConfiguration<GateOptions>();
                var gate = new GateNode(options.InitiallyOpen, options.BoundedCapacity);
                context.HasInput(gate.Input);
                context.HasInput("Open", gate.Open, signal: true);
                context.HasInput("Close", gate.Close, signal: true);
                context.HasOutput(gate.Output);
                context.HasEvents(gate.Events);
                return gate;
            });
    }

    internal static ValueTask<IFlowNode> CreateJsonWindowNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<WindowRoutingOptions>();
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentDefinition.Resources.Clock);
        var node = new JsonWindowNode(options, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateJsonCorrelationNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<CorrelationRoutingOptions>();
        var keySelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.KeySelector);
        var sideSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.SideSelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentDefinition.Resources.Clock);
        var node = new JsonCorrelationNode(
            options,
            keySelector,
            sideSelector,
            options.Engine,
            clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateJsonJoinNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<JoinRoutingOptions>();
        var leftSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.LeftKeySelector);
        var rightSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.RightKeySelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentDefinition.Resources.Clock);
        var node = new JsonJoinNode(
            options,
            leftSelector,
            rightSelector,
            options.Engine,
            clock);
        context.HasInput("Left", node.Left);
        context.HasInput("Right", node.Right);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}

/// <summary>Configuration for flow.gate; the standalone component needs no Composition dependency.</summary>
public sealed class GateOptions
{
    public bool InitiallyOpen { get; set; }
    public int BoundedCapacity { get; set; } = 128;
}
