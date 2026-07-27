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
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                JsonSchemaValidatorDescriptor
            ],
            ValidationComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor JsonSchemaValidatorDescriptor { get; } = new(
        ValidationComponentDefinition.Types.JsonSchemaValidator,
        CreateJsonSchemaValidatorNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(
                ValidationComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<JsonSchemaValidationResult>(
                ValidationComponentDefinition.Ports.Output)
        ],
        options: ValidationComponentDefinition.CreateOptions(ValidationComponentDefinition.Types.JsonSchemaValidator),
        resources: ValidationComponentDefinition.CreateResources(ValidationComponentDefinition.Types.JsonSchemaValidator));

    public static IServiceCollection AddValidationComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateJsonSchemaValidatorNode(
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

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    ValidationComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonSchemaValidationResult>(
                    ValidationComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
