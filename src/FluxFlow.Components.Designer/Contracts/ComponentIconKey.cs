namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentIconKey
{
    public ComponentIconKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component icon key cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
