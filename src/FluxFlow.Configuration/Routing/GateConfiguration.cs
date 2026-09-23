using FluxFlow.Configuration.Links;

namespace FluxFlow.Configuration.Routing;

/// <summary>Serializable Gate settings; port names and wiring belong to registration.</summary>
public sealed record GateConfiguration
{
    public OneOrManyLinks Input { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Output { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Open { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Close { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Events { get; init; } = OneOrManyLinks.Empty;

    public bool InitiallyOpen { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
