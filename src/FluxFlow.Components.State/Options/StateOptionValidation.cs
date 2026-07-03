using FluxFlow.Components.State.Contracts;

namespace FluxFlow.Components.State.Options;

internal static class StateOptionValidation
{
    public static string? NormalizeOptional(string? value)
        => StateContractNormalization.NormalizeOptional(value);

    public static string ValidateReducer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "state.reducer option 'reducer' is required.",
                nameof(value));
        }

        return value.Trim();
    }

    public static string? ValidateKeyExpression(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "state.reducer option 'keyExpression' cannot be empty when set.",
                nameof(value));
        }

        return value.Trim();
    }

    public static int ValidateBoundedCapacity(int value)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "state.reducer option 'boundedCapacity' must be greater than zero.");

    public static int ValidateMaxKeys(int value)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "state.reducer option 'maxKeys' must be zero or greater.");
}
