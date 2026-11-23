using System.Text.Json.Nodes;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using FluxFlow.Application.Addressing;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using FluxFlow.Authoring;
using FluxFlow.Environments;
using System.Text.Json;

namespace FluxFlow.Studio.UI.Features.Designer;

public sealed partial class DesignerGraphState
{
    // File identity belongs to the editor session, outside configuration and Undo/Redo snapshots.
    internal StudioFile? ProjectFile { get; set; }
    internal string ProjectFileName => ProjectFile?.Name ?? "application.fluxflow";
    internal string CompiledFileStem
    {
        get
        {
            var stem = Path.GetFileNameWithoutExtension(ProjectFile?.Name.Replace('\\', '/'))?.Trim();
            return string.IsNullOrEmpty(stem) ? "App" : stem;
        }
    }
    internal bool IsProjectOpen { get; set; } = true;
    internal long ProjectGeneration { get; private set; }

    public PortColors PortColorOverrides { get; private set; } = new();
    public EnvironmentProfile GetEnvironment() => ActiveEnvironment.Setup.Copy();

    public void SetEnvironment(EnvironmentProfile profile)
    {
        profile.Validate();
        ActiveEnvironment.Setup = profile.Copy();
        RecordEdit();
    }

    public void SetPortColorOverrides(PortColors colors)
    {
        colors.Validate();
        if (PortColorOverrides == colors) return;
        PortColorOverrides = colors;
        RecordEdit();
    }

    public string ToProjectJson()
    {
        var configuration = JsonNode.Parse(ToJson())!.AsObject();
        RememberPositions();
        RememberLinkPlacements();
        return new StudioProject
        {
            Configuration = configuration,
            ApplicationSection = _sectionPath,
            ActiveWorkflow = WorkflowName,
            PortColors = PortColorOverrides,
            Environment = GetEnvironment(),
            Environments = _environments.Select(item => item.Copy()).ToList(),
            ActiveEnvironment = ActiveEnvironmentId,
            Nodes = _positions.ToDictionary(pair => pair.Key,
                pair => new StudioNodeLayout(pair.Value.X, pair.Value.Y, _colors.GetValueOrDefault(pair.Key)), StringComparer.Ordinal),
            Links = _linkPlacements.ToDictionary(pair => pair.Key, pair => pair.Value.Select(link =>
                new StudioLinkLayout(link.Source.Value, link.Target.Value, link.IsError,
                    link.SourceSide.ToString(), link.TargetSide.ToString())).ToArray(), StringComparer.Ordinal)
        }.ToJson();
    }

    public string ExportConfiguration() => new DesignerConfiguration(ToJson(), _sectionPath).ToApplicationJson();

    public IReadOnlyList<ValidationMessageModel> LoadProject(string json)
    {
        var project = StudioProject.Read(json, ResourceAdapters);
        // Validate all editor metadata before replacing the open project.
        foreach (var address in project.Nodes.Keys) _ = ComponentAddress.Parse(address);
        var placements = project.Links.ToDictionary(pair => pair.Key, pair => pair.Value.Select(link =>
            new LinkPlacement(PortAddress.Parse(link.Source), PortAddress.Parse(link.Target), link.IsError,
                Enum.Parse<PortAlignment>(link.SourceSide), Enum.Parse<PortAlignment>(link.TargetSide))).ToArray(), StringComparer.Ordinal);
        var messages = LoadDocument(project.Configuration.ToJsonString(), project.ApplicationSection, project.ActiveWorkflow, reset: true);
        foreach (var (address, node) in project.Nodes)
        {
            _positions[address] = new Point(node.X, node.Y);
            if (node.Color is not null) _colors[address] = node.Color;
        }
        foreach (var (workflow, links) in placements) _linkPlacements[workflow] = links;
        PortColorOverrides = project.PortColors;
        _environments = project.Environments?.Select(item => item.Copy()).ToList()
            ?? [new() { Id = "local", Setup = project.Environment.Copy() }];
        ActiveEnvironmentId = project.ActiveEnvironment is { } id && _environments.Any(item => item.Id == id) ? id : _environments[0].Id;
        ApplyNodeLayout();
        RestoreLinkPlacements();
        Revision++;
        NotifyChanged();
        return messages;
    }

    private void ApplyNodeLayout()
    {
        foreach (var node in Diagram.Nodes.OfType<FlowNodeModel>())
        {
            var address = ComponentAddress.Create(WorkflowName, node.NodeName).Value;
            node.Color = _colors.GetValueOrDefault(address);
            if (_positions.TryGetValue(address, out var point)) node.SetPosition(point.X, point.Y);
            node.Refresh();
        }
    }
}
