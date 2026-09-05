namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentAttributeValue
{
    public ComponentAttributeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component attribute value cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
