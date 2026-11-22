using FluxFlow.Configuration.Expressions;

namespace FluxFlow.Configuration.Components.Filtering;

/// <summary>Selects an immutable string key with an expression; keys use ordinal comparison.</summary>
public sealed record DeduplicateConfiguration : ComponentConfiguration
{
    public string? Engine { get; set; }
    public ExpressionSource? KeySelector { get; set; }
    public bool IgnoreCase { get; set; }
    public TimeSpan Retention { get; set; }
    public int MaxKeys { get; set; } = 1024;
    public int OutputCapacity { get; set; } = 128;

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(Engine), nameof(KeySelector))]
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Engine);
        ArgumentNullException.ThrowIfNull(KeySelector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
        if (Retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Retention));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxKeys);
    }
}
