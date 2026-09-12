using System.Text.Json;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Assertions.Composition;

public static class AssertionsServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddAssertions(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(AssertionsComponents.Assertion);
    }

    internal static void ConfigureAssertion(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(AssertionsComponentDefinition.Options.Expression, isRequired: true);
        component.AddOption<string>(AssertionsComponentDefinition.Options.ExpressionId);
        component.AddOption<string>(AssertionsComponentDefinition.Options.ExpressionName);
        component.AddOption<string>(AssertionsComponentDefinition.Options.InputType);
        component.AddOption<int>(AssertionsComponentDefinition.Options.BoundedCapacity);
        component.AddOption<string>(AssertionsComponentDefinition.Options.Description);
        component.AddOption<string>(AssertionsComponentDefinition.Options.FailureMessage);
        component.AddResource<IFlowExpressionEngine>(AssertionsComponentDefinition.Resources.Engine, isRequired: true);
        component.AddResource<IFlowMapContextFactory<JsonElement>>(AssertionsComponentDefinition.Resources.ContextFactory);
        component.AddResource<TimeProvider>(AssertionsComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateJsonAssertionNode)
            .HasStandardPorts(FlowValueMaterializers.JsonElement.InputShape);
    }

    private static JsonAssertionNode CreateJsonAssertionNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<AssertionOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            AssertionsComponentDefinition.Resources.Engine);
        var contextFactory = context.GetResource<IFlowMapContextFactory<JsonElement>>(
            AssertionsComponentDefinition.Resources.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            AssertionsComponentDefinition.Resources.Clock);
        return new JsonAssertionNode(
            options,
            expressionEngine,
            contextFactory,
            clock);
    }
}
