namespace FluxFlow.Composition;

public sealed record LinkDefinition
{
    public required PortReference From { get; init; }

    public required PortReference To { get; init; }

    public override string ToString() => $"{From} -> {To}";
}
