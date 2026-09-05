using FluxFlow.Nodes;

namespace FluxFlow.ReportingSample;

/// <summary>A position in one consumer-owned capture, not a workflow revision.</summary>
public readonly record struct ApplicationReportCursor(Guid StreamId, long Sequence);

public enum ApplicationReportPhase { Runtime, Preparing, Active, Draining }

public static class ApplicationReportHeaders
{
    public const string SchemaVersion = "flow.event.schema_version";
}

/// <summary>Consumer archive metadata around the unchanged canonical message envelope.</summary>
public sealed record ApplicationReportEvent
{
    public required ApplicationReportCursor Cursor { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public string? Application { get; init; }
    public string? RevisionId { get; init; }
    /// <summary>Event schema version, independent of workflow revision; absent producer headers mean version one.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Consumer-defined classification; the library does not stamp reporting phases.</summary>
    public ApplicationReportPhase Phase { get; init; }
    public required FlowMessage Message { get; init; }
}
