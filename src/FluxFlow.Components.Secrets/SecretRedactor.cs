namespace FluxFlow.Components.Secrets;

public static class SecretRedactor
{
    public const string RedactedText = "[redacted]";

    private static readonly string[] SensitiveFragments =
    [
        "secret",
        "password",
        "pwd",
        "passphrase",
        "token",
        "credential",
        "key",
        "auth",
        "bearer",
        "connectionstring",
        "cert",
        "pin",
        "salt"
    ];

    public static string Redact(string? value)
        => value is null ? string.Empty : RedactedText;

    public static IReadOnlyDictionary<string, string> RedactValues(
        IReadOnlyDictionary<string, string> values,
        IEnumerable<string>? protectedKeys = null)
    {
        ArgumentNullException.ThrowIfNull(values);

        var keys = protectedKeys?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return values.ToDictionary(
            pair => pair.Key,
            pair => ShouldRedact(pair.Key, keys) ? RedactedText : pair.Value,
            StringComparer.Ordinal);
    }

    public static bool ShouldRedact(string? key, ISet<string>? protectedKeys = null)
    {
        if (key is null)
            return false;

        if (protectedKeys is not null && protectedKeys.Contains(key))
            return true;

        return SensitiveFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
