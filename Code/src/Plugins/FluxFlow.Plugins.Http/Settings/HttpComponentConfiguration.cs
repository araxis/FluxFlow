namespace FluxFlow.Configuration.Components.Http;

public sealed record HttpComponentConfiguration : ComponentConfiguration
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
    }

    public string Client { get; init; } = string.Empty;
    public int OutputCapacity { get; init; } = 128;
}
