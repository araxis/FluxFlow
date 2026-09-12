using System.Text.Json;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddRouting(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(RoutingComponents.Window)
            .AddComponent(RoutingComponents.Correlation)
            .AddComponent(RoutingComponents.Join);
    }

    internal static void ConfigureWindow(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(RoutingComponentDefinition.Options.InputType);
        component.AddOption<int>(RoutingComponentDefinition.Options.MaxItems);
        component.AddOption<int>(RoutingComponentDefinition.Options.TimeMilliseconds);
        component.AddOption<bool>(RoutingComponentDefinition.Options.EmitPartialOnCompletion);
        component.AddOption<int>(RoutingComponentDefinition.Options.BoundedCapacity);
        component.AddResource<TimeProvider>(RoutingComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateJsonWindowNode)
            .HasStandardPorts(FlowValueMaterializers.JsonElement.InputShape);
    }

    internal static void ConfigureCorrelation(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(RoutingComponentDefinition.Options.Engine);
        component.AddOption<string>(RoutingComponentDefinition.Options.KeyExpression);
        component.AddOption<string>(RoutingComponentDefinition.Options.SideExpression);
        AddDiagnostics(component);
        component.AddOption<string>(RoutingComponentDefinition.Options.InputType);
        component.AddOption<string>(RoutingComponentDefinition.Options.RequestSide);
        component.AddOption<string>(RoutingComponentDefinition.Options.ResponseSide);
        AddMatchingAndLimits(component);
        component.AddResource<Func<JsonElement, string?>>(RoutingComponentDefinition.Resources.KeySelector, isRequired: true);
        component.AddResource<Func<JsonElement, string?>>(RoutingComponentDefinition.Resources.SideSelector, isRequired: true);
        component.AddResource<TimeProvider>(RoutingComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateJsonCorrelationNode)
            .HasStandardPorts(FlowValueMaterializers.JsonElement.InputShape);
    }

    internal static void ConfigureJoin(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(RoutingComponentDefinition.Options.Engine);
        component.AddOption<string>(RoutingComponentDefinition.Options.LeftKeyExpression);
        component.AddOption<string>(RoutingComponentDefinition.Options.RightKeyExpression);
        AddDiagnostics(component);
        component.AddOption<string>(RoutingComponentDefinition.Options.LeftInputType);
        component.AddOption<string>(RoutingComponentDefinition.Options.RightInputType);
        AddMatchingAndLimits(component);
        component.AddResource<Func<JsonElement, string?>>(RoutingComponentDefinition.Resources.LeftKeySelector, isRequired: true);
        component.AddResource<Func<JsonElement, string?>>(RoutingComponentDefinition.Resources.RightKeySelector, isRequired: true);
        component.AddResource<TimeProvider>(RoutingComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateJsonJoinNode)
            .HasInput(RoutingComponentDefinition.Ports.Left, static node => node.Left, FlowValueMaterializers.JsonElement.InputShape)
            .HasInput(RoutingComponentDefinition.Ports.Right, static node => node.Right, FlowValueMaterializers.JsonElement.InputShape)
            .HasOutput(RoutingComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(RoutingComponentDefinition.Ports.Events, static node => node.Events);
    }

    private static void AddDiagnostics(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(RoutingComponentDefinition.Options.ExpressionId);
        component.AddOption<string>(RoutingComponentDefinition.Options.ExpressionName);
    }

    private static void AddMatchingAndLimits(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<bool>(RoutingComponentDefinition.Options.CaseSensitive);
        component.AddOption<int>(RoutingComponentDefinition.Options.TimeoutMilliseconds);
        component.AddOption<int>(RoutingComponentDefinition.Options.MaxPending);
        component.AddOption<int>(RoutingComponentDefinition.Options.BoundedCapacity);
    }

    private static JsonWindowNode CreateJsonWindowNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<WindowRoutingOptions>();
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentDefinition.Resources.Clock);
        return new JsonWindowNode(options, clock);
    }

    private static JsonCorrelationNode CreateJsonCorrelationNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<CorrelationRoutingOptions>();
        var keySelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.KeySelector);
        var sideSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.SideSelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentDefinition.Resources.Clock);
        return new JsonCorrelationNode(
            options,
            keySelector,
            sideSelector,
            options.Engine,
            clock);
    }

    private static JsonJoinNode CreateJsonJoinNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<JoinRoutingOptions>();
        var leftSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.LeftKeySelector);
        var rightSelector = context.GetRequiredResource<Func<JsonElement, string?>>(
            RoutingComponentDefinition.Resources.RightKeySelector);
        var clock = context.GetResource<TimeProvider>(
            RoutingComponentDefinition.Resources.Clock);
        return new JsonJoinNode(
            options,
            leftSelector,
            rightSelector,
            options.Engine,
            clock);
    }

}
