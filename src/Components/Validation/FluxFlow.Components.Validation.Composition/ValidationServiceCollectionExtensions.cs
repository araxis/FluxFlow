using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Validation.Composition;

public static class ValidationServiceCollectionExtensions
{
    public static IServiceCollection AddValidation(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(ValidationComponentDefinition.Types.JsonSchemaValidator, CreateJsonSchemaValidatorNode);
    }

    internal static ValueTask<IFlowNode> CreateJsonSchemaValidatorNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<JsonSchemaValidatorOptions>();
        var schema = options.LoadSchema();
        var selector = context.GetResource<IJsonSchemaValueSelector>(
            ValidationComponentDefinition.Resources.Selector);
        var clock = context.GetResource<TimeProvider>(
            ValidationComponentDefinition.Resources.Clock);
        var node = new JsonSchemaValidatorNode(
            schema,
            selector,
            options.EffectiveValueSelector,
            options.SchemaId,
            options.SchemaPath,
            clock,
            options);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
