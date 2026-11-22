using System.Text.Json;
using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

// Row storage and validation belong to the plugin; hosts present ordinary text and choice fields.
public interface IPropertyCollection
{
    string Name { get; }
    string ItemName { get; }
    JsonElement[] Read(IDictionary<string, JsonElement> configuration);
    void Write(IDictionary<string, JsonElement> configuration, IReadOnlyList<JsonElement> rows);
    IReadOnlyList<OptionEditorModel> Fields(JsonElement row, IReadOnlyDictionary<string, JsonElement> configuration);
    string Title(JsonElement row);
    string Detail(JsonElement row);
    JsonElement Edit(JsonElement row, IReadOnlyDictionary<string, string?> fields);
    void Validate(JsonElement value, IReadOnlyDictionary<string, JsonElement> configuration, string path);
}
