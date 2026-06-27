namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentOptionChoiceValue
{
    public ComponentOptionChoiceValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component option choice value cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
