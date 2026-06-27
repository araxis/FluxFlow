namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentPortGroup
{
    public ComponentPortGroup(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component port group cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
