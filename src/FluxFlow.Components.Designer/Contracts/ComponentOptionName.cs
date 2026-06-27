namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentOptionName
{
    public ComponentOptionName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component option name cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
