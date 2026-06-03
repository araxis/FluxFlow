namespace FluxFlow.Components.Secrets.Contracts;

public sealed class SecretValue
{
    public SecretValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    private readonly string _value;

    public string Reveal() => _value;

    public override string ToString() => SecretRedactor.RedactedText;
}
