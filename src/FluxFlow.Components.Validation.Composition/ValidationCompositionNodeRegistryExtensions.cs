using System.Text.Json;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Validation.Composition;

public static class ValidationCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterJsonSchemaValidator(
        this CompositionNodeRegistry registry,
        string nodeType = ValidationCompositionNodeTypes.JsonSchemaValidator)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            ValidationCompositionNodeTypes.JsonSchemaValidatorDescriptor,
            CreateJsonSchemaValidatorNode,
            inputs:
            [
                CompositionPorts.Metadata<JsonElement>(
                    ValidationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<JsonSchemaValidationResult>(
                    ValidationCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    private static ValueTask<ComposedNode> CreateJsonSchemaValidatorNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<JsonSchemaValidatorOptions>();
        var schema = options.LoadSchema();
        var selector = context.GetResource<IJsonSchemaValueSelector>(
            ValidationCompositionResourceNames.Selector);
        var clock = context.GetResource<TimeProvider>(
            ValidationCompositionResourceNames.Clock);
        var node = new JsonSchemaValidatorNode(
            schema,
            selector,
            options.EffectiveValueSelector,
            options.SchemaId,
            options.SchemaPath,
            clock,
            options);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<JsonElement>(
                    ValidationCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<JsonSchemaValidationResult>(
                    ValidationCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
