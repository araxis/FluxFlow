namespace FluxFlow.Components.Secrets.Contracts;

public sealed record SecretOptionResolution
{
    private string _optionPath = string.Empty;

    public required string OptionPath
    {
        get => _optionPath;
        init => _optionPath = value?.Trim() ?? string.Empty;
    }

    public SecretReference? Reference { get; init; }
    public SecretDescriptor? Descriptor { get; init; }
    public SecretValue? Value { get; init; }
    public SecretDiagnostic? Diagnostic { get; init; }
    public bool Resolved => Value is not null && Diagnostic is null;
    public bool NotProvided => Reference is null && Value is null && Diagnostic is null;

    public static SecretOptionResolution FromResult(
        SecretOptionReference option,
        SecretResolveResult result)
        => new()
        {
            OptionPath = option.OptionPath,
            Reference = option.Reference,
            Descriptor = result.Descriptor,
            Value = result.Value,
            Diagnostic = result.Diagnostic
        };

    public static SecretOptionResolution NotProvidedResult(SecretOptionReference option)
        => new()
        {
            OptionPath = option.OptionPath,
            Reference = option.Reference
        };

    public static SecretOptionResolution Failed(
        SecretOptionReference option,
        SecretDiagnostic diagnostic)
        => new()
        {
            OptionPath = option.OptionPath,
            Reference = option.Reference,
            Diagnostic = diagnostic
        };

    public override string ToString()
    {
        if (Diagnostic is not null)
            return Diagnostic.ToString();

        return Resolved
            ? $"Resolved secret option '{OptionPath}'."
            : $"Secret option '{OptionPath}' was not provided.";
    }
}
