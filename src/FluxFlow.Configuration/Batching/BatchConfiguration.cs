using FluxFlow.Configuration.Links;

namespace FluxFlow.Configuration.Batching;

public sealed record BatchConfiguration
{
    public OneOrManyLinks Input { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Output { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Events { get; init; } = OneOrManyLinks.Empty;

    public int BatchSize { get; init; }
    public TimeSpan MaxWait { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
