using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Expressions;

namespace FluxFlow.Configuration.Components.Routing;

/// <summary>Named route expressions; port links remain ordinary component properties.</summary>
public sealed record RouterConfiguration : ComponentConfiguration
{
    internal static IReadOnlyList<string> ReservedNames { get; } = ["Input", "Output", "Error", "Unmatched", "Events", "Type", "Engine", "Routes", "OutputCapacity"];

    public OneOrManyPortReferences Unmatched { get; init; } = OneOrManyPortReferences.Empty;

    public string? Engine { get; set; }
    public Dictionary<string, ExpressionSource> Routes { get; set; } = new();
    public int OutputCapacity { get; set; } = 128;

    public void Validate(FluxFlow.Application.Addressing.ComponentAddress address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Engine);
        if (OutputCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(OutputCapacity));
        if (Routes.Count == 0) throw new ArgumentException($"Router '{address}' requires at least one route.");
        foreach (var (name, expression) in Routes)
        {
            if (ReservedNames
                .Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Router '{address}' route '{name}' conflicts with a component property or port.");
            _ = address.Port(name);
            ArgumentNullException.ThrowIfNull(expression);
        }
    }
}
