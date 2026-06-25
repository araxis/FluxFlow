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

    public static Dictionary<string, object?> CopyVariables(
        Dictionary<string, object?>? source)
        => source is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(source, StringComparer.Ordinal);
}
