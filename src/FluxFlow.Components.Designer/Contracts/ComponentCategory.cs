namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentCategory
{
    public ComponentCategory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component category cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
