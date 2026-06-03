namespace FluxFlow.Components.Secrets.Contracts;

public readonly record struct SecretName
{
    public SecretName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Secret name cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value ?? string.Empty;
}
