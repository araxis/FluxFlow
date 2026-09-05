namespace FluxFlow.Engine.Events;

/// <summary>Bounds the application's live observational feed. History and reporting are consumer-owned.</summary>
public sealed record ApplicationEventOptions
{
    public string? Application { get; init; }
    public int Capacity { get; init; } = 256;
}
