namespace FluxFlow.Components.State.Contracts;

internal static class StateContractNormalization
{
    public static string NormalizeRequired(string value)
        => value?.Trim() ?? string.Empty;

    public static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
