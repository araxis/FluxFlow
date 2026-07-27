using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Validation.Composition;

public static class ValidationServiceCollectionExtensions
{
    internal static ComponentDescriptor JsonSchemaValidatorDescriptor { get; } = new(
        ValidationComponentTypes.JsonSchemaValidator,
        CreateJsonSchemaValidatorNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(
                ValidationComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<JsonSchemaValidationResult>(
                ValidationComponentPortNames.Output)
        ]);

    public static IServiceCollection AddValidationComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(JsonSchemaValidatorDescriptor);
        services.AddComponentDesignMetadataProvider<ValidationComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateJsonSchemaValidatorNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<JsonSchemaValidatorOptions>();
        var schema = options.LoadSchema();
        var selector = context.GetResource<IJsonSchemaValueSelector>(
            ValidationComponentResourceNames.Selector);
        var clock = context.GetResource<TimeProvider>(
            ValidationComponentResourceNames.Clock);
        var node = new JsonSchemaValidatorNode(
            schema,
            selector,
            options.EffectiveValueSelector,
            options.SchemaId,
            options.SchemaPath,
            clock,
            options);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ValidationComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonSchemaValidationResult>(
                    ValidationComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
