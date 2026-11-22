using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluxFlow.Studio.UI.Features.Designer.Settings;

namespace FluxFlow.Studio.UI.Features.Designer.Projects;

public sealed class StudioProject
{
    [JsonRequired] public string Format { get; init; } = "FluxFlow.Studio";
    [JsonRequired] public int Version { get; init; } = 1;
    [JsonRequired] public JsonObject Configuration { get; init; } = new();
    public string ApplicationSection { get; init; } = "";
    public string? ActiveWorkflow { get; init; }
    public PortColors PortColors { get; init; } = new();
    public FluxFlow.Environments.EnvironmentProfile Environment { get; set; } = new();
    public List<FluxFlow.Environments.EnvironmentDefinition>? Environments { get; init; }
    public string? ActiveEnvironment { get; init; }
    public Dictionary<string, StudioNodeLayout> Nodes { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, StudioLinkLayout[]> Links { get; init; } = new(StringComparer.Ordinal);

    public static StudioProject Read(string json) => Read(json, null);

    internal static StudioProject Read(string json, IEnumerable<FluxFlow.Environments.IResourceEnvironmentAdapter>? adapters)
    {
        var project = JsonSerializer.Deserialize(json, StudioJsonContext.Default.StudioProject)
            ?? throw new JsonException("Studio project must be an object.");
        if (project.Format != "FluxFlow.Studio" || project.Version != 1)
            throw new JsonException("Unsupported Studio project format or version.");
        if (project.Configuration is null || project.ApplicationSection is null || project.PortColors is null ||
            project.Nodes is null || project.Links is null || project.Environment is null)
            throw new JsonException("Studio project contains missing or null fields.");
        project.PortColors.Validate();
        project.Environment.Validate();
        if (project.Environments is { } environments)
        {
            if (environments.Count == 0 || environments.Any(item => item is null) ||
                environments.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != environments.Count ||
                environments.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != environments.Count)
                throw new JsonException("Environments must have unique identities and names.");
            foreach (var environment in environments) environment.Validate(adapters);
            if (project.ActiveEnvironment is { } active && !environments.Any(item => item.Id == active))
                throw new JsonException("The active environment is missing.");
        }
        foreach (var node in project.Nodes.Values)
        {
            if (node is null || !double.IsFinite(node.X) || !double.IsFinite(node.Y))
                throw new JsonException("Node positions must be finite numbers.");
            PortColors.ValidateColor(node.Color);
        }
        foreach (var links in project.Links.Values)
        {
            if (links is null) throw new JsonException("Link layout must be an array.");
            foreach (var link in links)
                if (link is null || link.Source is null || link.Target is null ||
                    link.SourceSide is not ("Left" or "Right") || link.TargetSide is not ("Left" or "Right"))
                    throw new JsonException("Link layout requires addresses and Left or Right sides.");
        }
        return project;
    }

    public string ToJson() => JsonSerializer.Serialize(this, StudioJsonContext.Default.StudioProject);
}

public sealed record StudioNodeLayout(double X, double Y, string? Color);
public sealed record StudioLinkLayout(string Source, string Target, bool IsError, string SourceSide, string TargetSide);
