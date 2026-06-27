namespace FluxFlow.Components.Designer.Contracts;

public readonly record struct ComponentMetadataText
{
    public ComponentMetadataText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Component metadata text cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
