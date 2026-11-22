using System.Diagnostics.CodeAnalysis;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Configuration.Sources;

namespace FluxFlow.Configuration.Components.Reducing;

/// <summary>Initial JSON state and expressions over state/input and previous/current values.</summary>
public sealed record ReducerConfiguration : ComponentConfiguration
{
    public string? Engine { get; set; }
    public JsonSource? InitialState { get; set; }
    public ExpressionSource? Reduce { get; set; }
    public ExpressionSource? AreEqual { get; set; }
    public int OutputCapacity { get; set; } = 128;

    [MemberNotNull(nameof(Engine), nameof(InitialState), nameof(Reduce), nameof(AreEqual))]
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Engine);
        ArgumentNullException.ThrowIfNull(InitialState);
        ArgumentNullException.ThrowIfNull(Reduce);
        ArgumentNullException.ThrowIfNull(AreEqual);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
    }
}
