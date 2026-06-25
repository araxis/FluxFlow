namespace FluxFlow.Components.Secrets.Contracts;

public sealed record SecretOptionReference
{
    private string _optionPath = string.Empty;
    private IReadOnlyDictionary<string, string>? _metadata = new Dictionary<string, string>();

    public required string OptionPath
    {
        get => _optionPath;
        init => _optionPath = value?.Trim() ?? string.Empty;
    }

    public SecretReference? Reference { get; init; }
    public bool Required { get; init; } = true;
    public IReadOnlyDictionary<string, string> Metadata
    {
        get => _metadata!;
        init => _metadata = SecretContractMap.NormalizeOrPreserveInvalid(value);
    }

    public override string ToString()
        => Reference is null
            ? OptionPath
            : $"{OptionPath}: {Reference}";
}
