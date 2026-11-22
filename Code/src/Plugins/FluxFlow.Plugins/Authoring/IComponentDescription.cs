using System.Text.Json;

namespace FluxFlow.Authoring;

/// <summary>Design-time metadata and creation defaults; registering a description never activates a runtime component.</summary>
public interface IComponentDescription
{
    PaletteItemModel Palette { get; }
    NodeInspectorModel Inspector { get; }
    string Documentation => ComponentDocumentation.Read(GetType());

    PaletteItemModel GetPresentation(IReadOnlyDictionary<string, JsonElement> configuration) => Palette;
    NodeInspectorModel GetInspector(IReadOnlyDictionary<string, JsonElement> configuration) => Inspector;

    /// <summary>Applies defaults only when adding a new node, never when opening configuration.</summary>
    void InitializeConfiguration(IDictionary<string, JsonElement> configuration) { }
}
