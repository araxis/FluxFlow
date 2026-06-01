using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Validation;

public sealed class ValidationComponentModule : IFlowNodeModule
{
    public ValidationComponentModule(ValidationComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                ValidationComponentTypes.JsonSchemaValidator,
                context => JsonSchemaValidatorNodeFactory.Create(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
