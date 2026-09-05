namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentPortName
{
    public ComponentPortName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component port name cannot be empty.", nameof(value));

        if (value.Contains('.'))
            throw new ArgumentException("Component port name cannot contain '.'.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
