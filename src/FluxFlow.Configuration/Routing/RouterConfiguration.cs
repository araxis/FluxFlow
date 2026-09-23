using FluxFlow.Configuration.Links;
using FluxFlow.Configuration.Expressions;

namespace FluxFlow.Configuration.Routing;

/// <summary>Named route expressions; port links remain ordinary component properties.</summary>
public sealed class RouterConfiguration
{
    public OneOrManyLinks Input { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Output { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Unmatched { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Events { get; init; } = OneOrManyLinks.Empty;

    public string? Engine { get; set; }
    public Dictionary<string, ExpressionSource> Routes { get; set; } = new();
    public int OutputCapacity { get; set; } = 128;
}
