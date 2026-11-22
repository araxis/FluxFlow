using System.Text.Json.Nodes;
using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

// Inline lists under a single configuration object. The plugin owns defaults and field semantics.
public sealed record PropertyObjectGroups(string Name, string Title, int MaximumItems,
    IReadOnlyList<PropertyObjectList> Lists, PropertyValueMap? Map = null);

public sealed record PropertyObjectList(string Name, string ItemName, string TitleField,
    IReadOnlyList<OptionEditorModel> Fields, Func<JsonObject> Create);

public sealed record PropertyValueMap(string Name, string ItemName, Func<JsonObject, string?> Summary, Func<string, JsonObject?, IPropertyValueDraft> Open);

public enum PropertyValueKind { Text, Multiline, Toggle, Empty, List, Map }

public interface IPropertyValueDraft
{
    string Name { get; set; }
    string Type { get; }
    IReadOnlyList<string> Types { get; }
    PropertyValueKind Kind { get; }
    string ValueLabel { get; }
    string Text { get; set; }
    IReadOnlyList<IPropertyValueDraft> Items { get; }
    void ChangeType(string type);
    void AddItem();
    void RemoveItem(IPropertyValueDraft item);
    JsonObject Save();
}
