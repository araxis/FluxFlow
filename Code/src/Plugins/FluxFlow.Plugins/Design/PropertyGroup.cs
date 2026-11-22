using System.Text.Json;
using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

public sealed record PropertyGroup(string Title, string? Path, IReadOnlyList<OptionEditorModel> Options)
{
    public Func<IReadOnlyDictionary<string, JsonElement>, IReadOnlyList<ProjectedField>>? Fields { get; init; }
    public bool AddBeforeEditing { get; init; }
    public Func<IReadOnlyDictionary<string, JsonElement>, string, bool>? IsVisible { get; init; }
    public Func<IReadOnlyDictionary<string, JsonElement>, bool>? ShowGroup { get; init; }
    public IReadOnlyList<PropertyStringMap> StringMaps { get; init; } = [];
    public IReadOnlyList<PropertyStringList> StringLists { get; init; } = [];
    public static JsonElement Field(IReadOnlyDictionary<string, JsonElement> values, string name) =>
        values.FirstOrDefault(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    public static OptionEditorModel EnumSelect<T>(string name, string label, T fallback) where T : struct, Enum =>
        OptionEditorModel.Create(name, label, OptionEditorKind.Select, defaultValue: fallback.ToString()) with
        { Choices = Enum.GetValues<T>().Select(value => OptionChoiceModel.FromEnum(value)).ToArray() };
    public static OptionEditorModel Select(string name, string label, string[] values, string? defaultValue = null) =>
        OptionEditorModel.Create(name, label, OptionEditorKind.Select, defaultValue: defaultValue) with
        { Choices = values.Select(value => new OptionChoiceModel { Value = value, DisplayName = value.Length == 0 ? "None" : value }).ToArray() };
}
public sealed record PropertyStringList(string Name, string Label, string EntryLabel, Func<string, string?>? Validate = null);
