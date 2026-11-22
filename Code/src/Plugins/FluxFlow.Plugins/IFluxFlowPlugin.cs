using FluxFlow.Authoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Plugins;

// The catalog is data only. Optional Studio UI code is delivered separately from runtime code.
public interface IFluxFlowPlugin
{
    PluginDescription Description { get; }
    void ConfigureWorkflow(IServiceCollection services);
    void ConfigureHost(IServiceCollection services) { }
    // Optional per-run host integration; existing plugins need not implement it.
    void ConfigureWorkflowHost(IHostBuilder host, IConfiguration configuration) { }
    IReadOnlyList<PluginFunction> Functions => [];
}

public sealed record PluginFunction(string Name, Delegate Implementation, string? Signature = null);
[System.Text.Json.Serialization.JsonSerializable(typeof(PluginDescription[]))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string[]))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
[System.Text.Json.Serialization.JsonSerializable(typeof(bool))]
[System.Text.Json.Serialization.JsonSerializable(typeof(int))]
public partial class PluginJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }

public sealed record PluginDescription(string Id, IReadOnlyList<PluginComponent> Components, IReadOnlyList<PluginConnection> Connections)
{
    public PluginDesignEntry? Design { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> ClientAssemblies => Design is null ? StudioDependencies : Design.Dependencies.Append(Design.Assembly);
    public string? StudioEntry { get; init; }
    // Private managed assemblies needed by the editor, in dependency order, from lib/<tfm>.
    public IReadOnlyList<string> StudioDependencies { get; init; } = [];
}
public sealed record PluginComponent(PaletteItemModel Palette, NodeInspectorModel Inspector, string Documentation,
    string? Editor = null) : IComponentDescription
{
    public string? HostRequirement { get; init; }
    public ComponentPreset? Preset { get; init; }
    public void InitializeConfiguration(IDictionary<string, System.Text.Json.JsonElement> configuration)
    {
        if (Preset is { } preset)
            configuration[preset.Field.Name] = System.Text.Json.JsonSerializer.SerializeToElement(preset.Value, PluginJsonContext.Default.String);
    }
    public NamedExpressionOutputs? NamedOutputs { get; init; }
    private IReadOnlyList<OptionEditorModel> Fields => Inspector.Sections.SelectMany(s => s.Options).ToArray();
    public PaletteItemModel GetPresentation(IReadOnlyDictionary<string, System.Text.Json.JsonElement> configuration)
    {
        var palette = NamedOutputs?.Apply(Palette, configuration) ?? Palette;
        if (Preset is { } preset && !preset.Matches(configuration))
            palette = palette with { DisplayName = preset.CustomDisplayName, Summary = preset.CustomSummary };
        if (!palette.Inputs.Concat(palette.Outputs).Any(port => port.ActiveWhen != null)) return palette;
        var fields = Fields;
        PortModel Resolve(PortModel port) => port.ActiveWhen?.Matches(fields, configuration) == false
            ? port with { InactiveReason = port.InactiveHint ?? "Inactive for these settings." } : port;
        return palette with { Inputs = palette.Inputs.Select(Resolve).ToArray(), Outputs = palette.Outputs.Select(Resolve).ToArray() };
    }
    public NodeInspectorModel GetInspector(IReadOnlyDictionary<string, System.Text.Json.JsonElement> configuration)
    {
        var fields = Fields;
        var inspector = Inspector;
        if (Preset is { } preset && !preset.Matches(configuration))
            inspector = inspector with { Sections = [inspector.Sections[0] with { Options = [preset.Field, .. inspector.Sections[0].Options] }, .. inspector.Sections.Skip(1)] };
        return fields.Any(f => f.VisibleWhen != null || f.MaxMinusField != null) ? inspector with { Sections = inspector.Sections
            .Select(s => s with { Options = s.Options.Where(f => f.VisibleWhen?.Matches(fields, configuration) != false)
                .Select(f => ResolveMaximum(f, fields, configuration)).ToArray() }).ToArray() } : inspector;
    }

    private static OptionEditorModel ResolveMaximum(OptionEditorModel field, IReadOnlyList<OptionEditorModel> fields,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> configuration)
    {
        if (field.MaxMinusField is not { } name || field.Max is not { } max) return field;
        var sibling = fields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (sibling is null) return field;
        var raw = configuration.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        var text = raw.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null
            ? sibling.DefaultValue?.ToString() : raw.ToString();
        return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            && (sibling.Min is null || value >= sibling.Min.Value) ? field with { Max = max - value } : field;
    }

}
public sealed record PluginConnection(string Type, string Title, string? EndpointProperty, bool WorkflowOnly,
    System.Text.Json.JsonElement Defaults, IReadOnlyList<OptionEditorModel> Options, string? Editor = null)
{
    // Existing plugins retain their reference rules unless they opt into local workflow ownership.
    public bool WorkflowResourcesAreLocal { get; init; }
    public string? InstanceIdentityProperty { get; init; }
    public bool EndpointIsUri { get; init; } = true;
    public IReadOnlyList<OptionSectionModel> Sections { get; init; } = [];
}
