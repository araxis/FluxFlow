namespace FluxFlow.Components.Secrets.Contracts;

public sealed record SecretRecord
{
    public required SecretDescriptor Descriptor { get; init; }
    public required SecretValue Value { get; init; }
}
