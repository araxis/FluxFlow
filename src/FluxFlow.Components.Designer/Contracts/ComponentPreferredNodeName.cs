namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentPreferredNodeName
{
    public ComponentPreferredNodeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component preferred node name cannot be empty.", nameof(value));

        if (value.Contains('.'))
            throw new ArgumentException("Component preferred node name cannot contain '.'.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
