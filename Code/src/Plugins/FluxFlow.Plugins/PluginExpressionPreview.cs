using System.Text.Json;

namespace FluxFlow.Plugins;

/// <summary>Compiles isolated preview text with declared component settings. The returned evaluator
/// receives a complete message envelope and returns the generated value. No workflow is started.</summary>
public delegate Func<JsonElement, JsonElement> PluginExpressionPreview(string expression,
    IReadOnlyDictionary<string, JsonElement> settings);
