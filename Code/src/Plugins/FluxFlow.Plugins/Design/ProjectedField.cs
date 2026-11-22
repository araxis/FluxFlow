using System.Text.Json;
using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

/// <summary>A view of a saved value, such as the address and address type encoded in one endpoint.</summary>
public sealed record ProjectedField(string Label, string Value)
{
    public Action<IDictionary<string, JsonElement>, string>? Write { get; init; }
    public Func<string?, string?>? Validate { get; init; }
    public IReadOnlyList<OptionChoiceModel> Choices { get; init; } = [];
    public string? Diagnostic { get; init; }
    public string? Hint { get; init; }
}
