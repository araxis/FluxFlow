using System.Diagnostics.CodeAnalysis;
using FluxFlow.Configuration.Expressions;

namespace FluxFlow.Configuration.Components.Mapping;

/// <summary>Data bound from configuration; executable mapping is supplied during Build.</summary>
public sealed record MapperConfiguration : ComponentConfiguration
{
    public string? Engine { get; set; }
    public ExpressionSource? Expression { get; set; }
    public bool HandleErrors { get; set; }
    public int OutputCapacity { get; set; } = 128;

    [MemberNotNull(nameof(Engine), nameof(Expression))]
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Engine);
        ArgumentNullException.ThrowIfNull(Expression);
        if (OutputCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(OutputCapacity));
    }
}
