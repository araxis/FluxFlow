using System.Text.Json;
using FluxFlow.Environments;

namespace FluxFlow.Studio.UI.Features.Observations.Events;

// A display projection only; the captured envelope remains the source of full event data.
public sealed class ExecutionEventRow(ExecutionOutput record) : IEquatable<ExecutionEventRow>
{
    public ExecutionOutput Record { get; } = record;
    public string Port => Record.Port;
    public string? InstanceId => Record.InstanceId;
    public string? Name => Text(Value, "Name");
    public string? Kind => Text(Value, "Kind");
    public string? Level => Text(Value, "Level");
    public string? Message => Text(Value, "Message") ?? Record.Error;
    public DateTimeOffset Timestamp => Date(Value, "Timestamp") ?? Date(Record.Message, "Timestamp") ?? Record.Timestamp;
    public string? FullData => Record.Message?.GetRawText() ?? Record.Error;

    // The Events grid is scoped to one run; polling deserializes new objects for retained sequences.
    public bool Equals(ExecutionEventRow? other) => other is not null && Record.Sequence == other.Record.Sequence;
    public override bool Equals(object? obj) => obj is ExecutionEventRow other && Equals(other);
    public override int GetHashCode() => Record.Sequence.GetHashCode();

    public static bool IsEvent(ExecutionOutput record) => record.Port.EndsWith(".Events", StringComparison.Ordinal);

    private JsonElement? Value => Property(Record.Message, "Value");
    private static JsonElement? Property(JsonElement? value, string name) =>
        value is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty(name, out var property) ? property : null;
    private static string? Text(JsonElement? value, string name) =>
        Property(value, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    private static DateTimeOffset? Date(JsonElement? value, string name) =>
        Property(value, name) is { ValueKind: JsonValueKind.String } text && text.TryGetDateTimeOffset(out var date) ? date : null;
}
