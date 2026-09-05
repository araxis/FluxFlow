namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentType
{
    public ComponentType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component type cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
