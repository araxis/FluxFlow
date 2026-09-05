namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentValueTypeHint
{
    public ComponentValueTypeHint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component value type hint cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
