using FluxFlow.Nodes;

namespace FluxFlow.Components.Designer.Contracts;

public enum EventFieldRole
{
    Dimension = 1,
    Measurement = 2,
    Detail = 3
}

public enum EventFieldValueKind
{
    Any = 0,
    String = 1,
    Number = 2,
    Boolean = 3,
    Object = 4,
    Array = 5
}

public sealed record EventFieldDesignMetadata
{
    public required string Path { get; init; }
    public required EventFieldRole Role { get; init; }
    public EventFieldValueKind ValueKind { get; init; } = EventFieldValueKind.Any;
    public string? Unit { get; init; }
    public bool IsSensitive { get; init; }
}

public sealed record EventDesignMetadata
{
    public required string Type { get; init; }
    public FlowEventKind Kind { get; init; } = FlowEventKind.Diagnostic;
    public int Version { get; init; } = 1;
    public IReadOnlyList<EventFieldDesignMetadata> Fields { get; init; } = [];
}
