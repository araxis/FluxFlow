namespace FluxFlow.Engine.Definitions;

public sealed record LinkDefinition
{
    public required PortAddress From { get; init; }
    public string? When { get; init; }
}
