namespace FluxFlow.Components.Configuration.Contracts;

public readonly record struct ConfigurationOptionPath
{
    public ConfigurationOptionPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Configuration option path cannot be empty.", nameof(value));

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
