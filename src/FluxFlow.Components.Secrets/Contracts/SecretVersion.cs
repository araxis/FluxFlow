namespace FluxFlow.Components.Secrets.Contracts;

public readonly record struct SecretVersion
{
    public SecretVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Secret version cannot be empty.", nameof(value));

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
