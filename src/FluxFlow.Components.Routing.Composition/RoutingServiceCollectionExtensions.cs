using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Nodes;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddRouting(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignedComponent(RoutingComponents.Window)
            .AddDesignedComponent(RoutingComponents.Correlation)
            .AddDesignedComponent(RoutingComponents.Join);
    }

    internal static void ConfigureWindow(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Window", "Buffers input messages into count- or time-based windows.", "panel-top", "window", 420);
        AddTypeName(component, RoutingComponentDefinition.Options.InputType, "Input Type");
        component.AddOption<int>(RoutingComponentDefinition.Options.MaxItems, OptionValueKind.Number, "Max Items", "Maximum buffered item count; set timeMilliseconds when zero.", defaultValue: 0, min: 0, section: "Windowing", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(RoutingComponentDefinition.Options.TimeMilliseconds, OptionValueKind.Number, "Time Milliseconds", "Maximum window duration in milliseconds; set maxItems when zero.", defaultValue: 0, min: 0, section: "Windowing", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<bool>(RoutingComponentDefinition.Options.EmitPartialOnCompletion, OptionValueKind.Boolean, "Emit Partial On Completion", "Emit a partial window when input completes.", defaultValue: true, section: "Windowing", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddCapacity(component);
        component.AddResource<TimeProvider>(RoutingComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic routing timing, timeouts, and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        component
            .UseFactory(CreateJsonWindowNode)
            .HasInput(RoutingComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Schema-less JSON value.", true)
            .HasOutput(RoutingComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 1, "Count-, time-, or completion-based window; failures use the message error case.", true)
            .HasEvents(RoutingComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort window diagnostics.");
    }

    internal static void ConfigureCorrelation(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Correlation", "Pairs related request and response messages by host-provided key and side selectors.", "link", "correlate", 460);
        AddEngine(component);
        AddExpression(component, RoutingComponentDefinition.Options.KeyExpression, "Key Expression", "Diagnostic key expression metadata; key selection uses the keySelector resource.", RoutingComponentDefinition.Resources.KeySelector);
        AddExpression(component, RoutingComponentDefinition.Options.SideExpression, "Side Expression", "Diagnostic side expression metadata; side selection uses the sideSelector resource.", RoutingComponentDefinition.Resources.SideSelector);
        AddDiagnostics(component);
        AddTypeName(component, RoutingComponentDefinition.Options.InputType, "Input Type");
        AddText(component, RoutingComponentDefinition.Options.RequestSide, "Request Side", "request", "Side label treated as the request side.");
        AddText(component, RoutingComponentDefinition.Options.ResponseSide, "Response Side", "response", "Side label treated as the response side.");
        AddMatchingAndLimits(component, "Match keys and sides using case-sensitive comparisons.", "Timeout for pending correlations.", "Maximum pending correlation keys.");
        component.AddResource<Func<JsonElement, string?>>(RoutingComponentDefinition.Resources.KeySelector, "Key Selector", 0, "Required keyed delegate that selects the correlation key for each input message.", true, "Func<JsonElement,string?>", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Delegate, keyPattern: "delegate:{name}");
        component.AddResource<Func<JsonElement, string?>>(RoutingComponentDefinition.Resources.SideSelector, "Side Selector", 1, "Required keyed delegate that selects request or response side labels.", true, "Func<JsonElement,string?>", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Delegate, keyPattern: "delegate:{name}");
        AddClock(component, 2);
        component.AddAttribute("requiredResources", $"{RoutingComponentDefinition.Resources.KeySelector},{RoutingComponentDefinition.Resources.SideSelector}");
        component
            .UseFactory(CreateJsonCorrelationNode)
            .HasInput(RoutingComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Schema-less JSON request or response value.", true)
            .HasOutput(RoutingComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 1, "Match or timeout outcome; failures use the message error case.", true)
            .HasEvents(RoutingComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort correlation diagnostics.");
    }

    internal static void ConfigureJoin(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Join", "Joins left and right messages by host-provided key selectors.", "combine", "join", 460);
        AddEngine(component);
        AddExpression(component, RoutingComponentDefinition.Options.LeftKeyExpression, "Left Key Expression", "Diagnostic left key expression metadata; left keys use the leftKeySelector resource.", RoutingComponentDefinition.Resources.LeftKeySelector);
        AddExpression(component, RoutingComponentDefinition.Options.RightKeyExpression, "Right Key Expression", "Diagnostic right key expression metadata; right keys use the rightKeySelector resource.", RoutingComponentDefinition.Resources.RightKeySelector);
        AddDiagnostics(component);
        AddTypeName(component, RoutingComponentDefinition.Options.LeftInputType, "Left Input Type");
        AddTypeName(component, RoutingComponentDefinition.Options.RightInputType, "Right Input Type");
        AddMatchingAndLimits(component, "Match keys using case-sensitive comparisons.", "Timeout for pending joins.", "Maximum pending join keys.");
        component.AddResource<Func<JsonElement, string?>>(RoutingComponentDefinition.Resources.LeftKeySelector, "Left Key Selector", 0, "Required keyed delegate that selects the join key for left messages.", true, "Func<JsonElement,string?>", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Delegate, keyPattern: "delegate:{name}");
        component.AddResource<Func<JsonElement, string?>>(RoutingComponentDefinition.Resources.RightKeySelector, "Right Key Selector", 1, "Required keyed delegate that selects the join key for right messages.", true, "Func<JsonElement,string?>", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Delegate, keyPattern: "delegate:{name}");
        AddClock(component, 2);
        component.AddAttribute("requiredResources", $"{RoutingComponentDefinition.Resources.LeftKeySelector},{RoutingComponentDefinition.Resources.RightKeySelector}");
        component
            .UseFactory(CreateJsonJoinNode)
            .HasInput(RoutingComponentDefinition.Ports.Left, static node => node.Left, "Left", "Messages", 0, "Schema-less JSON left value.", true)
            .HasInput(RoutingComponentDefinition.Ports.Right, static node => node.Right, "Right", "Messages", 1, "Schema-less JSON right value.")
            .HasOutput(RoutingComponentDefinition.Ports.Output, static node => node.Output, "Output", "Messages", 2, "Match or timeout outcome; failures use the message error case.", true)
            .HasEvents(RoutingComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 3, "Best-effort join diagnostics.");
    }

    private static void ConfigureCommon(ComponentRegistrationBuilder component, string displayName, string summary, string iconKey, string preferredNodeName, int width)
    {
        component.WithDisplay(displayName, "Routing", summary, iconKey, preferredNodeName, width);
    }

    private static void AddEngine(ComponentRegistrationBuilder component)
        => component.AddOption<string>(RoutingComponentDefinition.Options.Engine, OptionValueKind.Text, "Engine", "Diagnostic engine metadata; composition DI selection uses host-owned selector resources.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);

    private static void AddExpression(ComponentRegistrationBuilder component, string name, string displayName, string helperText, string resource)
        => component.AddOption<string>(name, OptionValueKind.Expression, displayName, helperText, section: "Selection", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Expression, syntax: OptionDesignMetadataAttributeValues.Expression, relatedResource: resource);

    private static void AddDiagnostics(ComponentRegistrationBuilder component)
    {
        component.AddOption<string>(RoutingComponentDefinition.Options.ExpressionId, OptionValueKind.Text, "Expression ID", "Optional diagnostic identifier emitted with routing diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(RoutingComponentDefinition.Options.ExpressionName, OptionValueKind.Text, "Expression Name", "Optional diagnostic name emitted with routing diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
    }

    private static void AddTypeName(ComponentRegistrationBuilder component, string name, string displayName)
        => component.AddOption<string>(name, OptionValueKind.Text, displayName, "Diagnostic input type metadata; CLR type comes from the closed registration.", defaultValue: WindowRoutingOptions.ObjectTypeName, section: "Type Metadata", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);

    private static void AddText(ComponentRegistrationBuilder component, string name, string displayName, string defaultValue, string helperText)
        => component.AddOption<string>(name, OptionValueKind.Text, displayName, helperText, defaultValue: defaultValue, section: "Matching", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);

    private static void AddMatchingAndLimits(ComponentRegistrationBuilder component, string caseHelper, string timeoutHelper, string pendingHelper)
    {
        component.AddOption<bool>(RoutingComponentDefinition.Options.CaseSensitive, OptionValueKind.Boolean, "Case Sensitive", caseHelper, defaultValue: true, section: "Matching", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<int>(RoutingComponentDefinition.Options.TimeoutMilliseconds, OptionValueKind.Number, "Timeout Milliseconds", timeoutHelper, defaultValue: 30_000, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(RoutingComponentDefinition.Options.MaxPending, OptionValueKind.Number, "Max Pending", pendingHelper, defaultValue: 1_024, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        AddCapacity(component);
    }

    private static void AddCapacity(ComponentRegistrationBuilder component)
        => component.AddOption<int>(RoutingComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static void AddClock(ComponentRegistrationBuilder component, int order)
        => component.AddResource<TimeProvider>(RoutingComponentDefinition.Resources.Clock, "Clock", order, "Optional keyed clock for deterministic routing timing, timeouts, and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");

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
