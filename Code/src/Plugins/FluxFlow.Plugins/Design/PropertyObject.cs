using System.Text.Json;
using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

// A structured setting with editable fields and a lossless JSON view.
public sealed record PropertyObject(string Name, string Label,
    Func<Dictionary<string, JsonElement>> Defaults,
    Func<IReadOnlyDictionary<string, JsonElement>, bool, IReadOnlyList<OptionSectionModel>> Sections,
    Action<JsonElement> ValidateJson);
