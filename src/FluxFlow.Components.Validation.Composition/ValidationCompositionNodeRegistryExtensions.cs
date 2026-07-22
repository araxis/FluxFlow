using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

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
            CreateFlowValueJsonSchemaValidatorNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    ValidationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<JsonSchemaFlowValueValidationResult>>(
                    ValidationCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    private static ValueTask<ComposedNode> CreateFlowValueJsonSchemaValidatorNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<JsonSchemaValidatorOptions>();
        var schema = options.LoadSchema();
        var selector = context.GetResource<IJsonSchemaFlowValueSelector>(
            ValidationCompositionResourceNames.Selector);
        var clock = context.GetResource<TimeProvider>(
            ValidationCompositionResourceNames.Clock);
        var node = new FlowValueJsonSchemaValidatorNode(
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
                CompositionPorts.Input<FlowValue>(
                    ValidationCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<JsonSchemaFlowValueValidationResult>>(
                    ValidationCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
