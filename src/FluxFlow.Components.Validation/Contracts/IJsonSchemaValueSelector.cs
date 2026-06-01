using FluxFlow.Components.Validation.Options;

namespace FluxFlow.Components.Validation.Contracts;

public interface IJsonSchemaValueSelector<TInput>
{
    object? Select(TInput input, JsonSchemaValidatorContext context);
}
