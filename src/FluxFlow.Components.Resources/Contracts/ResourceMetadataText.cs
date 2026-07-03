namespace FluxFlow.Components.Resources.Contracts;

public readonly record struct ResourceMetadataText
{
    public ResourceMetadataText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Resource metadata text cannot be empty.", nameof(value));

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}
