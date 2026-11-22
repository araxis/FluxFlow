using System.Text.Json.Nodes;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using FluxFlow.Application.Addressing;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using FluxFlow.Authoring;

namespace FluxFlow.Studio.UI.Features.Designer;

public sealed partial class DesignerGraphState
{
    public IReadOnlyList<IComponentDescription> Components { get; }
    public IComponentDescription? FindDescription(string type) =>
        Components.FirstOrDefault(component => component.Palette.ComponentType == type);
    public PaletteItemModel? FindPresentation(string type) => FindDescription(type)?.Palette;
    private PaletteItemModel? DescribeNode(ConfigurationNode node) =>
        FindDescription(node.Type)?.GetPresentation(node.Values);
    private DesignerConfiguration _document = new("{\"Workflows\":{\"main\":{}}}");
    private HashSet<string> _renderedNodes = [];
    private readonly HashSet<ComponentAddress> _unavailableNodes = [];
    private ConfigurationLink[] _renderedLinks = [];
    private string _sectionPath = "";
    private readonly Dictionary<string, Point> _positions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _colors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LinkPlacement[]> _linkPlacements = new(StringComparer.Ordinal);
    private readonly List<Snapshot> _history = [];
    private int _historyIndex = -1;
    private sealed record Snapshot(string Json, string Section, string Workflow, Dictionary<string, Point> Positions,
        Dictionary<string, LinkPlacement[]> LinkPlacements, Dictionary<string, string> Colors, string? SelectedNode, PortColors PortColors, string Environment);
    private sealed record LinkPlacement(PortAddress Source, PortAddress Target, bool IsError,
        PortAlignment SourceSide, PortAlignment TargetSide);

    internal IReadOnlyList<FluxFlow.Environments.IResourceEnvironmentAdapter> ResourceAdapters { get; }
    internal IReadOnlyDictionary<string, FluxFlow.Plugins.PropertyRules> PluginValidators { get; }

    public DesignerGraphState(IEnumerable<IComponentDescription> components,
        IEnumerable<FluxFlow.Environments.IResourceEnvironmentAdapter>? adapters = null,
        IEnumerable<FluxFlow.Studio.UI.Features.Plugins.PluginSettingsValidator>? validators = null)
    {
        ResourceAdapters = adapters?.ToArray() ?? [];
        PluginValidators = (validators ?? []).ToDictionary(v => v.Type, v => v.Rules);
        Components = components.ToArray();
        if (Components.GroupBy(component => component.Palette.ComponentType, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Register only one Studio description per component type.", nameof(components));
        Diagram = new BlazorDiagram();
        Diagram.RegisterComponent<FlowNodeModel, FlowNodeWidget>();
        Diagram.RegisterComponent<LinkModel, FlowLinkWidget>();
        Diagram.RegisterComponent<FlowLinkModel, FlowLinkWidget>();
        Diagram.SelectionChanged += OnSelectionChanged;
        Diagram.ContainerChanged += () =>
        {
            foreach (var node in Diagram.Nodes) node.ReinitializePorts();
        };
        Diagram.Links.Added += link =>
        {
            link.TargetMarker = LinkMarker.Arrow;
            if (link is LinkModel styled) styled.Color = styled.SelectedColor = "currentColor";
            link.TargetAttached += OnLinkTargetAttached;
        };
    }

    public BlazorDiagram Diagram { get; }
    public string WorkflowName { get; private set; } = "main";
    public string SectionPath => _sectionPath;
    public IReadOnlyList<string> WorkflowNames => _document.WorkflowNames.ToArray();
    public FlowNodeModel? SelectedNode { get; private set; }
    public FlowLinkModel? SelectedLink => Diagram.GetSelectedModels().OfType<FlowLinkModel>().LastOrDefault();
    public int NodeCount => Diagram.Nodes.Count;
    public bool HasSelection => Diagram.GetSelectedModels().Any();
    public bool CanUndo => _historyIndex > 0;
    public bool CanRedo => _historyIndex < _history.Count - 1;
    public event Action? Changed;
    public event Action<string>? LinkRejected;

    public void SetSelectedNodeColor(string? color)
    {
        if (SelectedNode is { } node) SetNodeColor(ComponentAddress.Create(WorkflowName, node.NodeName), color);
    }

    public bool SetNodeColor(ComponentAddress address, string? color)
    {
        PortColors.ValidateColor(color);
        if (!_document.Nodes(address.WorkflowName).Any(item => item.Address == address) &&
            !(address.WorkflowName == WorkflowName && Diagram.Nodes.OfType<FlowNodeModel>().Any(node => node.NodeName == address.ComponentName)))
            throw new ArgumentException("The component is no longer available.");
        var key = address.Value;
        if (_colors.GetValueOrDefault(key) == color) return false;
        if (color is null) _colors.Remove(key);
        else _colors[key] = color;
        if (address.WorkflowName == WorkflowName && Diagram.Nodes.OfType<FlowNodeModel>().FirstOrDefault(node => node.NodeName == address.ComponentName) is { } visible)
        { visible.Color = color; visible.Refresh(); }
        RecordEdit();
        return true;
    }

    public FlowNodeModel AddNode(IComponentDescription description) => AddNode(description, null);

    internal void ZoomToFit()
    {
        if (Diagram.Container is { Width: > 0, Height: > 0 } && Diagram.Nodes.All(node => node.Size is not null))
            Diagram.ZoomToFit(60);
    }

    public FlowNodeModel AddNode(IComponentDescription description, Point? position)
    {
        var item = description.Palette;
        _document.EnsureWorkflow(WorkflowName);
        var names = _document.Nodes(WorkflowName).Select(node => node.Address.ComponentName)
            .Concat(Diagram.Nodes.OfType<FlowNodeModel>().Select(node => node.NodeName)).ToHashSet(StringComparer.Ordinal);
        var number = 1;
        var prefix = item.PreferredNodeName ?? item.ComponentType;
        while (names.Contains($"{prefix}-{number}")) number++;
        var node = new FlowNodeModel($"{prefix}-{number}", description, position ?? DesignerGraphMapper.NextPalettePosition(Diagram));
        description.InitializeConfiguration(node.Configuration);
        node.Title = node.Presentation.DisplayName;
        Diagram.Nodes.Add(node);
        NotifyChanged();
        return node;
    }

    public void DeleteSelected()
    {
        foreach (var link in Diagram.GetSelectedModels().OfType<BaseLinkModel>().ToArray()) Diagram.Links.Remove(link);
        foreach (var node in Diagram.GetSelectedModels().OfType<NodeModel>().ToArray()) Diagram.Nodes.Remove(node);
        SelectedNode = null;
        NotifyChanged();
    }

    public void Clear()
    {
        Diagram.Links.Clear();
        Diagram.Nodes.Clear();
        SelectedNode = null;
        NotifyChanged();
    }

    public string ToJson()
    {
        var selectedLink = LinkSelection();
        var previousJson = _document.ToJson();
        var nodes = Diagram.Nodes.OfType<FlowNodeModel>().ToArray();
        var removed = _renderedNodes.Except(nodes.Select(node => node.NodeName))
            .Select(name => ComponentAddress.Create(WorkflowName, name)).ToArray();
        foreach (var node in nodes)
            _document.UpdateNode(new ConfigurationNode(ComponentAddress.Create(WorkflowName, node.NodeName), node.ComponentType, node.Configuration));

        var current = Diagram.Links.Select(link => (Link: link,
            IsError: link.Source is SinglePortAnchor { Port: FlowPortModel { IsError: true } },
            Source: DesignerGraphMapper.Endpoint(link.Source, WorkflowName), Target: DesignerGraphMapper.Endpoint(link.Target, WorkflowName)))
            .Where(item => item.Source is not null && item.Target is not null).ToArray();
        var linksChanged = removed.Length != 0;
        foreach (var original in _renderedLinks)
            if (!current.Any(item => item.Link is FlowLinkModel loaded && ReferenceEquals(loaded.Original, original)
                && item.Source == original.Source && item.Target == original.Target && item.IsError == original.IsError))
            {
                _document.RemoveLink(original);
                linksChanged = true;
            }
        foreach (var item in current)
        {
            var original = (item.Link as FlowLinkModel)?.Original;
            if (original is null || original.Source != item.Source || original.Target != item.Target || original.IsError != item.IsError)
            {
                _document.AddLink(item.Source!, item.Target!, original, item.IsError);
                linksChanged = true;
            }
        }
        // Apply cleanup after canvas settings/links, so stale node settings cannot restore deleted references.
        _document.RemoveNodes(removed, FindPresentation, DescribeNode);
        if (removed.Length != 0 && _historyIndex >= 0)
            _history[_historyIndex] = _history[_historyIndex] with
            {
                Workflow = WorkflowName,
                SelectedNode = removed.Length == 1 ? removed[0].ComponentName : null
            };
        foreach (var address in removed)
        {
            _colors.Remove(address.Value);
            _positions.Remove(address.Value);
        }
        if (removed.Length != 0 && _unavailableNodes.Count != 0 && _linkPlacements.TryGetValue(WorkflowName, out var placements))
            _linkPlacements[WorkflowName] = placements.Where(link =>
                !removed.Contains(link.Source.ComponentAddress) && !removed.Contains(link.Target.ComponentAddress)).ToArray();
        var json = _document.ToJson();
        // Selection and movement do not change declarations or require new canvas models.
        if (json == previousJson && !linksChanged) return json;
        // Refresh declaration identities after edits while retaining the visible geometry and selection.
        var positions = nodes.ToDictionary(node => node.NodeName, node => node.Position, StringComparer.Ordinal);
        var selection = SelectedNode?.NodeName;
        LoadDocument(json, _sectionPath, WorkflowName);
        foreach (var node in Diagram.Nodes.OfType<FlowNodeModel>())
        {
            if (positions.TryGetValue(node.NodeName, out var position)) node.SetPosition(position.X, position.Y);
            if (node.NodeName == selection) Diagram.SelectModel(node, true);
        }
        // Array positions are not identities after adding, removing or reconnecting links.
        if (!linksChanged) SelectLink(selectedLink);
        return json;
    }

    public bool UpdateSelectedLink(string? condition, string? engine)
    {
        ToJson();
        var link = SelectedLink ?? throw new InvalidOperationException("Select a link to edit.");
        var selection = LinkSelection();
        if (!_document.UpdateLink(link.Original, condition, engine)) return false;
        LoadDocument(_document.ToJson(), _sectionPath, WorkflowName);
        SelectLink(selection);
        return true;
    }

    public bool RenameSelectedNode(string name)
    {
        ToJson();
        var node = SelectedNode ?? throw new InvalidOperationException("Select a component to rename.");
        var address = ComponentAddress.Create(WorkflowName, node.NodeName);
        var candidate = new DesignerConfiguration(_document.ToJson(), _sectionPath);
        if (!candidate.RenameNode(address, name, FindPresentation, DescribeNode)) return false;
        var renamed = ComponentAddress.Create(WorkflowName, name);
        if (_historyIndex >= 0)
            _history[_historyIndex] = _history[_historyIndex] with { Workflow = WorkflowName, SelectedNode = node.NodeName };
        if (_colors.Remove(address.Value, out var color)) _colors[renamed.Value] = color;
        node.NodeName = name;
        // The existing geometry capture now sees the renamed endpoints, including mirrored handles.
        LoadDocument(candidate.ToJson(), _sectionPath, WorkflowName);
        _positions.Remove(address.Value);
        Diagram.SelectModel(Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == name), true);
        RecordEdit();
        return true;
    }

    private (ComponentAddress Owner, string Property, int Index)? LinkSelection()
    {
        if (SelectedLink?.Original is not { } link) return null;
        return (link.Owner, link.Property, link.Declaration.Parent is JsonArray array
            ? array.IndexOf(link.Declaration) : -1);
    }

    private void SelectLink((ComponentAddress Owner, string Property, int Index)? selection)
    {
        if (selection is not { } location) return;
        var link = Diagram.Links.OfType<FlowLinkModel>().FirstOrDefault(item =>
            item.Original.Owner == location.Owner && item.Original.Property == location.Property &&
            (item.Original.Declaration.Parent is JsonArray array
                ? array.IndexOf(item.Original.Declaration) : -1) == location.Index);
        if (link is not null) Diagram.SelectModel(link, true);
    }

    public IReadOnlyList<ValidationMessageModel> LoadJson(string json, string sectionPath = "", string? workflow = null)
    {
        var messages = LoadDocument(json, sectionPath, workflow, reset: true);
        Revision++;
        return messages;
    }

    private IReadOnlyList<ValidationMessageModel> LoadDocument(string json, string sectionPath, string? workflow, bool reset = false,
        bool establishLayout = true)
    {
        var document = new DesignerConfiguration(json, sectionPath);
        var links = document.Links(FindPresentation, DescribeNode); // Validate before changing the current document/canvas.
        if (reset) { _positions.Clear(); _linkPlacements.Clear(); _colors.Clear(); PortColorOverrides = new(); ResetEnvironments(); }
        else { RememberPositions(); RememberLinkPlacements(); }
        _document = document;
        _sectionPath = sectionPath;
        WorkflowName = workflow is not null && document.WorkflowNames.Contains(workflow) ? workflow
            : document.WorkflowNames.FirstOrDefault() ?? "main";
        _unavailableNodes.Clear();
        var messages = document.WorkflowNames.Any()
            ? DesignerGraphMapper.Load(Diagram, document, WorkflowName, links, FindDescription, _unavailableNodes) : [];
        if (!document.WorkflowNames.Any()) Clear();
        _renderedNodes = Diagram.Nodes.OfType<FlowNodeModel>().Select(node => node.NodeName).ToHashSet(StringComparer.Ordinal);
        _renderedLinks = Diagram.Links.OfType<FlowLinkModel>().Select(link => link.Original).ToArray();
        foreach (var node in Diagram.Nodes.OfType<FlowNodeModel>())
        {
            var key = ComponentAddress.Create(WorkflowName, node.NodeName).Value;
            node.Color = _colors.GetValueOrDefault(key);
            if (_positions.TryGetValue(key, out var position))
                node.SetPosition(position.X, position.Y);
            else
            {
                // Opening an unvisited canvas establishes its initial geometry, not an edit.
                position = new Point(node.Position.X, node.Position.Y);
                _positions.Add(key, position);
                if (!reset && establishLayout) foreach (var snapshot in _history) snapshot.Positions.TryAdd(key, position);
            }
        }
        RestoreLinkPlacements();
        if (!_linkPlacements.ContainsKey(WorkflowName))
        {
            // First viewing a workflow establishes geometry, just like its node positions.
            RememberLinkPlacements();
            if (!reset && establishLayout) foreach (var snapshot in _history)
                snapshot.LinkPlacements.TryAdd(WorkflowName, _linkPlacements[WorkflowName]);
        }
        SelectedNode = null;
        NotifyChanged();
        return messages;
    }

    public void SelectWorkflow(string workflow)
    {
        if (!WorkflowNames.Contains(workflow)) throw new ArgumentException("Unknown workflow.", nameof(workflow));
        if (workflow != WorkflowName) LoadDocument(ToJson(), _sectionPath, workflow);
    }

    public void AddWorkflow(string name)
    {
        _ = ComponentAddress.Create(name, "validation");
        if (WorkflowNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("A workflow with that name already exists.", nameof(name));
        ToJson();
        _document.EnsureWorkflow(name);
        LoadDocument(_document.ToJson(), _sectionPath, name);
    }

    // Editor-only history and geometry never enter runtime configuration.
    public void ResetHistory()
    {
        Revision++;
        _history.Clear();
        _historyIndex = -1;
        RecordEdit();
    }

    public void RecordEdit()
    {
        var json = ToJson();
        RememberPositions();
        RememberLinkPlacements();
        var snapshot = new Snapshot(json, _sectionPath, WorkflowName, new(_positions, StringComparer.Ordinal),
            new(_linkPlacements, StringComparer.Ordinal), new(_colors, StringComparer.Ordinal), SelectedNode?.NodeName, PortColorOverrides, EnvironmentJson);
        if (_historyIndex >= 0 && _history[_historyIndex] is var previous && previous.Json == json && previous.PortColors == snapshot.PortColors && previous.Environment == snapshot.Environment &&
            previous.Colors.Count == snapshot.Colors.Count && previous.Colors.All(pair =>
                snapshot.Colors.TryGetValue(pair.Key, out var color) && color == pair.Value) &&
            previous.LinkPlacements.Count == snapshot.LinkPlacements.Count && previous.LinkPlacements.All(pair =>
                snapshot.LinkPlacements.TryGetValue(pair.Key, out var placements) && pair.Value.SequenceEqual(placements)) &&
            previous.Positions.Count == snapshot.Positions.Count && previous.Positions.All(pair =>
                snapshot.Positions.TryGetValue(pair.Key, out var point) && point.X == pair.Value.X && point.Y == pair.Value.Y)) return;
        _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(snapshot);
        if (_history.Count > 50) _history.RemoveAt(0);
        _historyIndex = _history.Count - 1;
        Revision++;
        NotifyChanged();
    }

    public void Undo() { if (CanUndo) { Revision++; Restore(_history[--_historyIndex]); } }
    public void Redo() { if (CanRedo) { Revision++; Restore(_history[++_historyIndex]); } }

    private void Restore(Snapshot snapshot)
    {
        var applyingEdits = _applyingEdits;
        _applyingEdits = true;
        try
        {
            var selection = LinkSelection();
            var selectedNode = SelectedNode?.NodeName;
            var selected = SelectedLink?.Original;
            var links = _renderedLinks;
            var environmentId = ActiveEnvironmentId;
            LoadDocument(snapshot.Json, snapshot.Section, snapshot.Workflow, reset: true);
            ActiveEnvironmentId = environmentId;
            if (!Diagram.Nodes.OfType<FlowNodeModel>().Any(node => node.NodeName == selectedNode))
                selectedNode = snapshot.SelectedNode;
            _positions.Clear();
            foreach (var pair in snapshot.Positions) _positions.Add(pair.Key, pair.Value);
            foreach (var pair in snapshot.Colors) _colors.Add(pair.Key, pair.Value);
            PortColorOverrides = snapshot.PortColors;
            RestoreEnvironments(snapshot.Environment);
            ApplyNodeLayout();
            foreach (var node in Diagram.Nodes.OfType<FlowNodeModel>())
            {
                if (node.NodeName == selectedNode) Diagram.SelectModel(node, true);
            }
            _linkPlacements.Clear();
            foreach (var pair in snapshot.LinkPlacements) _linkPlacements.Add(pair.Key, pair.Value);
            RestoreLinkPlacements();
            // Retain selection for a field edit only when the other declarations still match.
            // Duplicate endpoints can otherwise put a different link at the same array position.
            if (selection is not null && links.Length == _renderedLinks.Length && links.Zip(_renderedLinks).All(pair =>
                pair.First.Source == pair.Second.Source && pair.First.Target == pair.Second.Target &&
                pair.First.Declaration.GetPath() == pair.Second.Declaration.GetPath() &&
                (ReferenceEquals(pair.First, selected) || JsonNode.DeepEquals(pair.First.Declaration, pair.Second.Declaration))))
                SelectLink(selection);
        }
        finally { _applyingEdits = applyingEdits; }
        NotifyChanged();
    }

    private void RememberPositions()
    {
        foreach (var node in Diagram.Nodes.OfType<FlowNodeModel>())
            _positions[ComponentAddress.Create(WorkflowName, node.NodeName).Value] = new Point(node.Position.X, node.Position.Y);
    }

    private void RememberLinkPlacements()
    {
        var visible = Diagram.Links
            .Where(link => link.Source is SinglePortAnchor { Port: FlowPortModel } && link.Target is SinglePortAnchor { Port: FlowPortModel })
            .Select(link => new LinkPlacement(DesignerGraphMapper.Endpoint(link.Source, WorkflowName)!,
                DesignerGraphMapper.Endpoint(link.Target, WorkflowName)!,
                ((FlowPortModel)((SinglePortAnchor)link.Source).Port).IsError,
                ((SinglePortAnchor)link.Source).Port.Alignment, ((SinglePortAnchor)link.Target).Port.Alignment)).ToArray();
        if (_unavailableNodes.Count == 0 || !_linkPlacements.TryGetValue(WorkflowName, out var previous))
        {
            _linkPlacements[WorkflowName] = visible;
            return;
        }

        // Missing plugins cannot render their ports. Keep their saved anchor sides in place
        // while updating the links this canvas can actually edit.
        var merged = new List<LinkPlacement>();
        var next = 0;
        foreach (var link in previous)
        {
            if ((link.Source.ComponentAddress is { } source && _unavailableNodes.Contains(source)) ||
                (link.Target.ComponentAddress is { } target && _unavailableNodes.Contains(target)))
                merged.Add(link);
            else if (next < visible.Length) merged.Add(visible[next++]);
        }
        merged.AddRange(visible.Skip(next));
        _linkPlacements[WorkflowName] = merged.ToArray();
    }

    private void RestoreLinkPlacements()
    {
        if (!_linkPlacements.TryGetValue(WorkflowName, out var saved)) return;
        var remaining = saved.ToList();
        foreach (var link in Diagram.Links.OfType<FlowLinkModel>())
        {
            var index = remaining.FindIndex(item => item.Source == link.Original.Source &&
                item.Target == link.Original.Target && item.IsError == link.Original.IsError);
            if (index < 0) continue;
            var placement = remaining[index];
            remaining.RemoveAt(index);
            link.SetSource(OnSide(link.Source, placement.SourceSide));
            link.SetTarget(OnSide(link.Target, placement.TargetSide));
        }

        static SinglePortAnchor OnSide(Anchor anchor, PortAlignment side)
        {
            var port = (FlowPortModel)((SinglePortAnchor)anchor).Port;
            return new SinglePortAnchor(port.Parent.Ports.OfType<FlowPortModel>().Single(other =>
                other.Name == port.Name && other.IsInput == port.IsInput && other.IsError == port.IsError && other.Alignment == side));
        }
    }

    private void OnSelectionChanged(SelectableModel _) { SelectedNode = Diagram.GetSelectedModels().OfType<FlowNodeModel>().LastOrDefault(); NotifyChanged(); }
    private void OnLinkTargetAttached(BaseLinkModel link)
    {
        if (link.Source is not SinglePortAnchor source || link.Target is not SinglePortAnchor target) return;
        if (source.Port is FlowPortModel from && target.Port is FlowPortModel to && from.IsInput != to.IsInput)
        {
            if (from.IsInput)
            {
                link.SetSource(target);
                link.SetTarget(source);
            }
            NotifyChanged();
            return;
        }
        Diagram.Links.Remove(link);
        LinkRejected?.Invoke("Connect an output to an input.");
    }
}
