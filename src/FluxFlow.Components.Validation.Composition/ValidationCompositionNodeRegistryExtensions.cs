using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;

namespace FluxFlow.Components.Validation.Composition;

public static class ValidationCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterJsonSchemaValidator<TInput>(
        this CompositionNodeRegistry registry,
        string nodeType = ValidationCompositionNodeTypes.JsonSchemaValidator)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateJsonSchemaValidatorNode<TInput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    ValidationCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<JsonSchemaValidationResult<TInput>>(
                    ValidationCompositionPortNames.Output),
                CompositionPorts.Metadata<TInput>(
                    ValidationCompositionPortNames.Valid),
                CompositionPorts.Metadata<TInput>(
                    ValidationCompositionPortNames.Invalid)
            ]);
    }

    private static ValueTask<ComposedNode> CreateJsonSchemaValidatorNode<TInput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<JsonSchemaValidatorOptions>();
        var schema = options.LoadSchema();
        var selector = context.GetResource<IJsonSchemaValueSelector<TInput>>(
            ValidationCompositionResourceNames.Selector);
        var clock = context.GetResource<TimeProvider>(
            ValidationCompositionResourceNames.Clock);
        var node = new JsonSchemaValidatorNode<TInput>(
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
                CompositionPorts.Input<TInput>(
                    ValidationCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<JsonSchemaValidationResult<TInput>>(
                    ValidationCompositionPortNames.Output,
                    node.Output),
                CompositionPorts.Output<TInput>(
                    ValidationCompositionPortNames.Valid,
                    node.Valid),
                CompositionPorts.Output<TInput>(
                    ValidationCompositionPortNames.Invalid,
                    node.Invalid)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
