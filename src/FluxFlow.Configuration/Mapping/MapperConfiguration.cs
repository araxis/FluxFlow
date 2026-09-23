using FluxFlow.Configuration.Links;
using FluxFlow.Configuration.Expressions;

namespace FluxFlow.Configuration.Mapping;

/// <summary>Data bound from configuration; executable mapping is supplied during Build.</summary>
public sealed class MapperConfiguration
{
    public OneOrManyLinks Input { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Output { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Events { get; init; } = OneOrManyLinks.Empty;

    public string? Engine { get; set; }
    public ExpressionSource? Expression { get; set; }
    public int OutputCapacity { get; set; } = 128;
}
