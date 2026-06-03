namespace FluxFlow.Components.Resources.Contracts;

public readonly record struct ResourceName
{
    public ResourceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Resource name cannot be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}
