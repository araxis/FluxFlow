using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using FluxFlow.Authoring;
using FluxFlow.Environments;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using FluxFlow.Studio.UI.Features.Designer.Components.Workspace;
using FluxFlow.Studio.UI.Features.Observations;

namespace FluxFlow.Studio.UI.Features.Environments.Topology;

public partial class EnvironmentTopologyPage
{
    [Parameter] public Features.Designer.DesignerGraphState? WorkspaceGraph { get; set; }
    [Parameter] public bool InfrastructureMode { get; set; }
    private Features.Designer.DesignerGraphState Graph => WorkspaceGraph ?? ProjectGraph;
    [Parameter] public bool Hidden { get; set; }
    [Parameter] public EventCallback Back { get; set; }
    [Parameter] public EventCallback RunRequested { get; set; }
    [Parameter] public EventCallback StopRequested { get; set; }
    [Parameter] public string? RunEnvironmentId { get; set; }
    [Parameter] public EventCallback<string> RunEnvironmentChanged { get; set; }
    [Parameter] public EventCallback<string> OpenConnection { get; set; }
    [Parameter] public EventCallback<ApplicationTelemetryTarget> OnShowApplicationLogs { get; set; }
    [Parameter] public EventCallback<ApplicationTelemetryTarget> OnShowApplicationTraces { get; set; }
    private Task ShowApplicationObservations(ApplicationTelemetryTarget target, bool traces) =>
        !_disposed && !Hidden && Run.ResolveApplicationTelemetry(Graph, target.InstanceId, target.RunId) is { } current
            ? (traces ? OnShowApplicationTraces : OnShowApplicationLogs).InvokeAsync(current) : Task.CompletedTask;
    private readonly BlazorDiagram _diagram = new();
    private DockWorkspace _workspace = default!;
    internal DockWorkspace Workspace => _workspace;
    internal void FitCanvas() => _diagram.ZoomToFit(60);
    private readonly DockLayout _layout = new(() => new()
    {
        Horizontal = true, Percent = 22, First = new("Components"),
        Second = new() { Horizontal = true, Percent = 62, First = new("Canvas"), Second = new("Inspector") }
    });
    private EnvironmentDefinition _definition = new();
    private string? _selection, _dragType;
    private bool _loading, _disposed;
    [Inject] private CanvasViewState CanvasView { get; set; } = default!;
    private long _revision = -1;
    private IDisposable? _edit;
    private (TopologyNodeModel Node, string Edge, double X, double Y, Point Position, Blazor.Diagrams.Core.Geometry.Size Size)? _resize;
    private EnvironmentNode? Selected => _definition.Topology.Nodes.FirstOrDefault(node => node.Id == _selection);
    private EnvironmentCheckResult? _revealCheck;
    private async Task CheckEnvironment()
    {
        await Run.CheckAsync(Graph);
        if (!_disposed && !Hidden && Run.CheckRevision == Graph.Revision && Run.CheckResult is { Success: true } result)
            Snackbar.Add(result.Message, Severity.Success);
    }
    internal void ShowCheckResult(EnvironmentCheckResult check)
    {
        if (_disposed || Hidden || Run.ResolveCheckNode(Graph, check) is not { } id ||
            _diagram.Nodes.OfType<TopologyNodeModel>().FirstOrDefault(node => node.Definition.Id == id) is not { } node) return;
        _diagram.SelectModel(node, true);
        _workspace.Show("Inspector", DockPosition.Right);
        _workspace.Show("Canvas", DockPosition.Left);
        _revealCheck = check;
        StateHasChanged();
    }

    internal void ShowEnvironmentProperties()
    {
        if (_disposed) return;
        _diagram.UnselectAll();
        _selection = null;
        _workspace.Show("Inspector", DockPosition.Right);
        StateHasChanged();
    }
    internal bool ShowServiceSetup(string path)
    {
        if (_disposed || Hidden) return false;
        var node = _diagram.Nodes.OfType<TopologyNodeModel>().FirstOrDefault(node =>
            node.Definition.Kind == EnvironmentNodeKind.Service && string.Equals(node.Definition.Resource, path, StringComparison.OrdinalIgnoreCase));
        if (node is null) return false;
        _diagram.SelectModel(node, true);
        _workspace.Show("Inspector", DockPosition.Right);
        StateHasChanged();
        return true;
    }
    private void SetTimeout(int seconds)
    {
        var profile = Graph.GetEnvironment();
        if (!profile.TrySetStartupTimeout(seconds)) return;
        using var edit = Graph.BeginManualEdit();
        Graph.SetEnvironment(profile);
    }
    protected override void OnAfterRender(bool firstRender) => RevealCheckTarget();

    internal void RevealCheckTarget()
    {
        var check = _revealCheck;
        if (_disposed || check is null || Hidden || Run.ResolveCheckNode(Graph, check) is not { } id ||
            _diagram.Nodes.OfType<TopologyNodeModel>().FirstOrDefault(node => node.Definition.Id == id) is not { Selected: true } node)
        { _revealCheck = null; return; }
        if (_diagram.Container is not { Width: > 0, Height: > 0 } viewport) return;
        _revealCheck = null;
        var zoom = _diagram.Zoom;
        var x = node.Position.X * zoom + _diagram.Pan.X;
        var y = node.Position.Y * zoom + _diagram.Pan.Y;
        var width = (node.Size?.Width ?? 0) * zoom;
        var height = (node.Size?.Height ?? 0) * zoom;
        if (x >= 0 && y >= 0 && x + width <= viewport.Width && y + height <= viewport.Height) return;
        _diagram.SetPan(viewport.Width / 2 - (node.Position.X + (node.Size?.Width ?? 0) / 2) * zoom,
            viewport.Height / 2 - (node.Position.Y + (node.Size?.Height ?? 0) / 2) * zoom);
    }
    private static readonly Dictionary<string, (EnvironmentNodeKind Kind, string Name, string Icon)> BuiltIns = new()
    {
        ["application"] = (EnvironmentNodeKind.Application, "Application", Icons.Material.Outlined.Settings),
        ["host"] = (EnvironmentNodeKind.Host, "Execution host", Icons.Material.Outlined.Computer)
    };
    private IReadOnlyList<PaletteItemModel> Palette => InfrastructureMode ? InfrastructurePalette : BuiltIns.Select(pair => new PaletteItemModel
        { ComponentType = pair.Key, DisplayName = pair.Value.Name, Icon = pair.Value.Icon,
            DefaultColor = TopologyNodeModel.DefaultColor(pair.Value.Kind),
            Category = "Compute" })
        .Concat(Graph.Connections.Select(resource => new PaletteItemModel { ComponentType = "service:" + resource.Path,
            DisplayName = resource.Name, Summary = resource.Type, Category = "Services", Icon = Icons.Material.Outlined.Dns, DefaultColor = TopologyNodeModel.DefaultColor(EnvironmentNodeKind.Service) }))
        .Concat(Preferences.Value.Environments.Infrastructure.Select(host => new PaletteItemModel
        { ComponentType = "infrastructure:" + host.Id, DisplayName = host.Name, Summary = $"{host.CreateTopology().Nodes.Count(node => node.Kind == EnvironmentNodeKind.Service)} existing services",
            Category = "Saved hosts", Icon = Icons.Material.Outlined.Computer, DefaultColor = TopologyNodeModel.DefaultColor(EnvironmentNodeKind.Host) })).ToArray();

    private PaletteItemModel[] InfrastructurePalette =>
    [
        .. Graph.ResourceAdapters.Select(a => a.Infrastructure).OfType<InfrastructureServiceType>().Where(type => AvailableConnections.Any(connection => connection.Type == type.Type)).Select(item => new PaletteItemModel { ComponentType = "existing:" + item.Type,
            DisplayName = item.Name, Summary = item.Help, Category = "Existing services", Icon = TopologyConnections.Icon(item.Type, Graph.ResourceAdapters), DefaultColor = "#68a7ef" })
    ];
    protected override void OnInitialized()
    {
        _diagram.Options.Constraints.ShouldDeleteNode = node => ValueTask.FromResult(CanRemove(node));
        _diagram.RegisterComponent<TopologyNodeModel, TopologyNodeWidget>();
        _diagram.SelectionChanged += SelectionChanged;
        _diagram.PointerUp += PointerUp;
        _diagram.ZoomChanged += Refresh;
        _diagram.ContainerChanged += RefreshPorts;
        Graph.Changed += Changed;
        Run.Changed += RunChanged;
        Preferences.Changed += Refresh;
        Rebuild();
    }
    private void Refresh() => _ = InvokeAsync(() => { if (!_disposed) StateHasChanged(); });
    private void RunChanged() => _ = InvokeAsync(() => { if (!_disposed) { RefreshEndpoints(); StateHasChanged(); } });
    private void RefreshEndpoints()
    {
        var resources = Graph.Connections;
        var status = InfrastructureMode ? null : Run.CurrentTopologyStatus(Graph);
        var services = status is { State: "Running" } ? status.Services : [];
        foreach (var node in _diagram.Nodes.OfType<TopologyNodeModel>())
        {
            var state = TopologyRunState.Resolve(node.Definition, _definition, status);
            var logTarget = status is not null && node.Definition.Kind == EnvironmentNodeKind.Application
                ? new ApplicationTelemetryTarget(status.Id, node.Definition.Id, node.Definition.Name) : null;
            var endpoint = node.Definition.Kind == EnvironmentNodeKind.Service ? TopologyEndpoint.Resolve(_definition, node.Definition,
                resources.FirstOrDefault(resource => resource.Path.Equals(node.Definition.Resource, StringComparison.OrdinalIgnoreCase)),
                services.FirstOrDefault(service => service.Resource.Equals(node.Definition.Resource, StringComparison.OrdinalIgnoreCase)), EndpointAdapters) : null;
            if (node.Endpoint == endpoint && node.RunState == state && node.TelemetryTarget == logTarget) continue;
            if (!node.IsContainer && (node.RunState is null) != (state is null))
            {
                node.SetContentSize(new Blazor.Diagrams.Core.Geometry.Size(180, 60 + node.ConnectionPorts.Count() * 24 + (state is null ? 0 : 20)));
                node.ReinitializePorts();
                node.RefreshLinks();
            }
            node.Endpoint = endpoint;
            node.RunState = state;
            node.TelemetryTarget = logTarget;
            node.Refresh();
        }
    }
    private TopologyEndpoint? SelectedEndpoint => _diagram.Nodes.OfType<TopologyNodeModel>()
        .FirstOrDefault(node => node.Definition.Id == _selection)?.Endpoint;
    private TopologyRunState? SelectedRunState => _diagram.Nodes.OfType<TopologyNodeModel>()
        .FirstOrDefault(node => node.Definition.Id == _selection)?.RunState;
    private void RefreshPorts()
    {
        foreach (var node in _diagram.Nodes) node.ReinitializePorts();
        if (_revealCheck is not null) Refresh();
    }
    private void Changed() => _ = InvokeAsync(() =>
    {
        if (_disposed) return;
        if (!InfrastructureMode && Run.CheckRevision is { } revision && revision != Graph.Revision) Run.CancelCheck();
        Rebuild();
        StateHasChanged();
    });
    private static string KindTitle(EnvironmentNode node) => node.Kind == EnvironmentNodeKind.Host ? "Execution host"
        : node.Kind == EnvironmentNodeKind.Network ? "Network" : node.Kind == EnvironmentNodeKind.Application ? "Application" : "Service";
    private void Rebuild()
    {
        if (_revision == Graph.Revision) return;
        _revision = Graph.Revision;
        _loading = true;
        try
        {
            if (_definition.Id != Graph.ActiveEnvironmentId)
            {
                EndEdit();
                _dragType = null;
                _selection = null;
            }
            if (_resize is not null) EndEdit();
            _resize = null;
            _definition = Graph.GetEnvironmentDefinition();
            _diagram.Links.Clear(); _diagram.Nodes.Clear();
            foreach (var entry in _definition.Topology.Nodes.Where(node => node.Kind != EnvironmentNodeKind.Network).OrderBy(node => node.Kind == EnvironmentNodeKind.Host ? 0 : 1))
            {
                var icon = BuiltIns.Values.FirstOrDefault(item => item.Kind == entry.Kind).Icon ?? Icons.Material.Outlined.Dns;
                if (entry.ExistingService is { } service)
                    icon = TopologyConnections.Icon(service.Type, Graph.ResourceAdapters);
                var node = new TopologyNodeModel(entry, icon, KindTitle(entry));
                node.Sequence = _definition.Topology.Nodes.IndexOf(entry);
                node.ResizeStarted = StartResize;
                node.ShowLogs = target => ShowApplicationObservations(target, false);
                node.ShowTraces = target => ShowApplicationObservations(target, true);
                if (entry.ExistingService is { } existing)
                    node.AddConnectionPort(entry.Id, existing.Type, existing.Name);
                foreach (var connection in Graph.Connections.Where(connection => TopologyConnections.Supported(connection.Type, Graph.ResourceAdapters) &&
                    (entry.Kind == EnvironmentNodeKind.Application || entry.Kind == EnvironmentNodeKind.Service &&
                        string.Equals(entry.Resource, connection.Path, StringComparison.OrdinalIgnoreCase))))
                    node.AddConnectionPort(connection.Path, connection.Type, TopologyConnections.Label(connection, Graph.ResourceAdapters));
                _diagram.Nodes.Add(node);
                if (entry.Id == _selection) _diagram.SelectModel(node, true);
            }
            if (Selected is null)
            {
                _selection = InfrastructureMode ? _definition.Topology.Nodes.FirstOrDefault(node => node.Kind == EnvironmentNodeKind.Host)?.Id : null;
                if (_diagram.Nodes.OfType<TopologyNodeModel>().FirstOrDefault(node => node.Definition.Id == _selection) is { } root)
                    _diagram.SelectModel(root, true);
            }
            var nodes = _diagram.Nodes.OfType<TopologyNodeModel>().ToDictionary(node => node.Definition.Id);
            TopologyContainers.Arrange(nodes.Values.ToArray());
            foreach (var app in nodes.Values.Where(node => node.Definition.Kind == EnvironmentNodeKind.Application))
                foreach (var port in app.ConnectionPorts)
                {
                    var targetId = _definition.Topology.BindingTarget(app.Definition, port.Path);
                    if (targetId is null || !nodes.TryGetValue(targetId, out var service)) continue;
                    var target = service.ConnectionPorts.FirstOrDefault(candidate => candidate.Type == port.Type);
                    if (target is null || !port.CanAttachTo(target)) continue;
                    var link = new LinkModel(new SinglePortAnchor(port), new SinglePortAnchor(target))
                        { Color = "var(--mud-palette-primary)", TargetMarker = LinkMarker.Arrow };
                    link.Labels.Add(new LinkLabelModel(link, port.Label));
                    _diagram.Links.Add(link);
                    port.InitialTargetId = service.Definition.Id;
                }
        }
        finally { _loading = false; }
        RefreshEndpoints();
    }
    private void SelectionChanged(SelectableModel model)
    {
        if (_loading) return;
        _selection = _diagram.GetSelectedModels().OfType<TopologyNodeModel>().LastOrDefault()?.Definition.Id;
        Refresh();
    }
    private void BeginEdit() => _edit ??= Graph.BeginManualEdit();
    private void EndEdit() { _edit?.Dispose(); _edit = null; }
    private void StartResize(TopologyNodeModel node, string edge, PointerEventArgs args)
    {
        if (_disposed || args.Button != 0 || !node.CanResize) return;
        BeginEdit();
        _diagram.SelectModel(node, true);
        _resize = (node, edge, args.ClientX, args.ClientY, node.Position, node.Size!);
        Refresh();
    }
    private void Resize(PointerEventArgs args)
    {
        if (_resize is not { } resize || Graph.Revision != _revision) return;
        var dx = (args.ClientX - resize.X) / _diagram.Zoom;
        var dy = (args.ClientY - resize.Y) / _diagram.Zoom;
        resize.Node.ResizeBounds(resize.Position.X + (resize.Edge.Contains('w') ? dx : 0),
            resize.Position.Y + (resize.Edge.Contains('n') ? dy : 0),
            resize.Position.X + resize.Size.Width + (resize.Edge.Contains('e') ? dx : 0),
            resize.Position.Y + resize.Size.Height + (resize.Edge.Contains('s') ? dy : 0), resize.Edge);
        TopologyContainers.KeepInside(resize.Node);
    }
    private void FinishResize()
    {
        if (_resize is null) return;
        _resize = null;
        try { CommitCanvas(); } finally { EndEdit(); }
    }
    private void CancelResize()
    {
        _resize = null;
        EndEdit();
        _revision = -1;
        Rebuild();
    }
    private async void PointerUp(Model? model, Blazor.Diagrams.Core.Events.PointerEventArgs args)
    {
        var environmentId = Graph.ActiveEnvironmentId;
        await InvokeAsync(async () =>
        {
            await Task.Yield();
            if (_disposed || environmentId != Graph.ActiveEnvironmentId) { EndEdit(); return; }
            try
            {
                var nodes = _diagram.Nodes.OfType<TopologyNodeModel>().ToArray();
                var moved = nodes.Where(node => node.Selected && node.HasMoved).ToArray();
                foreach (var node in moved)
                    TopologyContainers.Drop(node, nodes);
                CommitCanvas();
            }
            finally { EndEdit(); }
        });
    }
    private void CommitCanvas()
    {
        if (_disposed || _loading || Graph.Revision != _revision) return;
        try
        {
            var topology = CaptureTopology(_diagram, _definition.Topology, Graph.Connections);
            if (InfrastructureMode) (new InfrastructureHost { Topology = topology }).Validate(Graph.ResourceAdapters);
            Graph.SetTopology(topology);
        }
        catch (ArgumentException error)
        {
            Snackbar.Add(error.Message, Severity.Warning);
            _revision = -1;
            Rebuild();
        }
    }
    internal static EnvironmentTopology CaptureTopology(BlazorDiagram diagram, EnvironmentTopology? saved = null,
        IReadOnlyList<ConfigurationResource>? connections = null)
    {
        var allNodes = diagram.Nodes.OfType<TopologyNodeModel>().ToArray();
        var livePorts = allNodes.SelectMany(node => node.ConnectionPorts).ToHashSet();
        foreach (var link in diagram.Links)
            if ((link.Source.Model is TopologyConnectionPort source && !livePorts.Contains(source)) ||
                (link.Target.Model is TopologyConnectionPort target && !livePorts.Contains(target)))
                throw new ArgumentException("This binding no longer belongs to the current canvas.");
        var nodes = allNodes.Where(node => node.Definition.InfrastructureGroup is not { } group || allNodes.Any(root => root.Definition.Id == group)).ToArray();
        var ids = nodes.Select(node => node.Definition.Id).ToHashSet();
        var topology = new EnvironmentTopology { Nodes = PreserveNetworks(nodes.OrderBy(node => node.Sequence).Select(node => node.Capture() with
        {
            Host = node.Definition.Host is { } host && ids.Contains(host) ? host : null
        }).ToList(), saved ?? new()) };
        foreach (var node in nodes.Where(node => node.Definition.Kind == EnvironmentNodeKind.Application))
            CaptureBindings(node, diagram, topology, connections ?? []);
        return topology;
    }
    private static List<EnvironmentNode> PreserveNetworks(List<EnvironmentNode> visible, EnvironmentTopology saved)
    {
        visible = visible.Select(node => node with { Networks = [.. node.Networks] }).ToList();
        var ids = visible.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var retained = saved.Nodes.Where(node => node.Kind == EnvironmentNodeKind.Network && !ids.Contains(node.Id) &&
            (node.InfrastructureGroup is null || ids.Contains(node.InfrastructureGroup)));
        var all = visible.Concat(retained.Select(node => node with
        {
            Host = node.Host is { } host && ids.Contains(host) ? host : null,
            Networks = [.. node.Networks]
        })).ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (var node in visible) node.Networks.RemoveAll(id => !all.ContainsKey(id));
        return saved.Nodes.Where(node => all.ContainsKey(node.Id)).Select(node => all[node.Id])
            .Concat(visible.Where(node => !saved.Nodes.Any(original => original.Id == node.Id))).ToList();
    }
    private static void CaptureBindings(TopologyNodeModel node, BlazorDiagram diagram, EnvironmentTopology topology,
        IReadOnlyList<ConfigurationResource> connections)
    {
        foreach (var port in node.ConnectionPorts.Where(port => port.IsApplication))
        {
            var targets = new List<TopologyConnectionPort>();
            foreach (var link in diagram.Links)
            {
                var other = link.Source.Model == port ? link.Target.Model : link.Target.Model == port ? link.Source.Model : null;
                if (other is not TopologyConnectionPort target) continue;
                if (target.IsApplication || target.Type != port.Type) throw new ArgumentException($"'{port.Label}' requires a compatible service.");
                targets.Add(target);
            }
            if (targets.Count > 1) throw new ArgumentException($"'{port.Label}' can bind to only one service.");
            var targetId = targets.SingleOrDefault()?.Parent is TopologyNodeModel service ? service.Definition.Id : null;
            if (targetId != port.InitialTargetId)
                topology.SetBinding(node.Definition.Id, port.Path, TopologyBinding.Explicit(targetId), connections.ConnectionType);
        }
    }
    private bool Disabled(string type) =>
        type.StartsWith("service:", StringComparison.Ordinal) && _definition.Topology.Nodes.Any(node => string.Equals(node.Resource, type[8..], StringComparison.OrdinalIgnoreCase));
    private void Add(string type) => Add(type, null);
    private void Add(string type, Point? point)
    {
        if (_disposed || Disabled(type)) return;
        var topology = Graph.GetEnvironmentDefinition().Topology;
        var position = point ?? new Point(80 + topology.Nodes.Count % 3 * 240, 80 + topology.Nodes.Count / 3 * 180);
        if (type.StartsWith("infrastructure:", StringComparison.Ordinal))
        {
            var host = Preferences.Value.Environments.Infrastructure.FirstOrDefault(item => "infrastructure:" + item.Id == type);
            if (host is null) return;
            _selection = topology.PlaceHost(host, position.X, position.Y, Graph.ResourceAdapters);
            Graph.SetTopology(topology);
            return;
        }
        EnvironmentNode node;
        if (BuiltIns.TryGetValue(type, out var item))
        {
            var local = item.Kind == EnvironmentNodeKind.Host && !topology.Nodes.Any(node => node.LocalHost);
            var name = local ? "This computer" : item.Name;
            for (var suffix = 2; topology.Nodes.Any(node => node.Name == name); suffix++) name = $"{item.Name} {suffix}";
            node = new() { Kind = item.Kind, Name = name,
                LocalHost = local, X = position.X, Y = position.Y,
                Width = item.Kind is EnvironmentNodeKind.Host or EnvironmentNodeKind.Network ? 520 : null,
                Height = item.Kind is EnvironmentNodeKind.Host or EnvironmentNodeKind.Network ? 360 : null };
        }
        else if (InfrastructureMode && type.StartsWith("existing:", StringComparison.Ordinal))
        {
            var description = InfrastructureServiceTypes.Find(type[9..], Graph.ResourceAdapters);
            if (description is null) return;
            var service = new InfrastructureService { Name = description.Name, Type = description.Type,
                Endpoint = description.DefaultEndpoint };
            node = new() { Kind = EnvironmentNodeKind.Service, Name = service.Name, ExistingService = service, X = position.X, Y = position.Y };
        }
        else
        {
            var connection = Graph.Connections.FirstOrDefault(resource => "service:" + resource.Path == type);
            if (connection is null) return;
            node = new() { Kind = EnvironmentNodeKind.Service, Name = connection.Name, Resource = connection.Path, X = position.X, Y = position.Y };
        }
        topology.Nodes.Add(node);
        if (InfrastructureMode) node.Host = topology.Nodes.Single(item => item.Kind == EnvironmentNodeKind.Host).Id;
        var selectedHost = Selected?.Kind == EnvironmentNodeKind.Host ? Selected.Id : Selected?.Host;
        _selection = node.Id;
        if (point is not null || InfrastructureMode || node.Kind == EnvironmentNodeKind.Application)
        {
            var model = new TopologyNodeModel(node, "", "");
            var nodes = _diagram.Nodes.OfType<TopologyNodeModel>().Append(model).ToList();
            if (point is not null || InfrastructureMode) TopologyContainers.Drop(model, nodes);
            if (node.Kind == EnvironmentNodeKind.Application && model.Definition.Host is null)
            {
                var host = nodes.FirstOrDefault(item => item.Definition.Kind == EnvironmentNodeKind.Host && item.Definition.Id == selectedHost)
                    ?? nodes.FirstOrDefault(item => item.Definition.LocalHost);
                if (host is null)
                {
                    host = new TopologyNodeModel(new() { Kind = EnvironmentNodeKind.Host, Name = "This computer", LocalHost = true,
                        X = position.X, Y = position.Y, Width = 520, Height = 360 }, "", "");
                    nodes.Add(host);
                }
                model.Definition.Host = host.Definition.Id;
                TopologyContainers.Arrange(nodes);
                model.SetPosition(host.Position.X + 32 * host.DisplayScale, host.Position.Y + 80 * host.DisplayScale);
                TopologyContainers.KeepInside(model);
            }
            if (InfrastructureMode && model.Definition.Host is null)
            {
                model.Definition.Host = topology.Nodes.Single(item => item.Kind == EnvironmentNodeKind.Host).Id;
                TopologyContainers.Arrange(nodes);
                TopologyContainers.KeepInside(model);
            }
            topology = topology with { Nodes = PreserveNetworks(nodes.Select(item => item.Capture()).ToList(), topology) };
        }
        Graph.SetTopology(topology);
    }
    private void Drop(DragEventArgs args)
    {
        if (_dragType is not { } type) return;
        _dragType = null;
        Add(type, _diagram.GetRelativeMousePoint(args.ClientX, args.ClientY));
    }
    private void Remove()
    {
        foreach (var link in _diagram.GetSelectedModels().OfType<BaseLinkModel>().Where(link => !link.Locked).ToArray()) _diagram.Links.Remove(link);
        foreach (var node in _diagram.GetSelectedModels().OfType<TopologyNodeModel>().Where(CanRemove).ToArray())
        {
            if (node.Definition.InfrastructureGroup is { } group)
                foreach (var child in _diagram.Nodes.OfType<TopologyNodeModel>().Where(child => child.Definition.InfrastructureGroup == group && child != node).ToArray()) _diagram.Nodes.Remove(child);
            _diagram.Nodes.Remove(node);
        }
        CommitCanvas();
    }
    private bool CanRemove(SelectableModel model) => model is not TopologyNodeModel node ? !model.Locked :
        !(InfrastructureMode && node.Definition.Kind == EnvironmentNodeKind.Host) &&
        (node.Definition.InfrastructureGroup is null || node.Definition.InfrastructureGroup == node.Definition.Id);
    private async Task KeyDown(KeyboardEventArgs args)
    {
        if (args.CtrlKey && args.Key.Equals("z", StringComparison.OrdinalIgnoreCase)) Graph.Undo();
        else if (args.CtrlKey && args.Key.Equals("y", StringComparison.OrdinalIgnoreCase)) Graph.Redo();
        else if (args.Key is "Delete" or "Backspace")
        {
            await Task.Yield();
            if (!_disposed) CommitCanvas();
        }
    }
    private void EditProperties(string environmentId, string? selection, Action edit, long? revision = null)
    {
        if (!_disposed && !Hidden && Graph.ActiveEnvironmentId == environmentId && _selection == selection &&
            (revision is null || revision == Graph.Revision)) edit();
    }
    private void SetColor(string? color)
    {
        if (_selection is null) return;
        var topology = Graph.GetEnvironmentDefinition().Topology;
        if (topology.SetColor(_selection, color)) Graph.SetTopology(topology);
    }
    private void RenameNode(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (InfrastructureMode && Selected?.ExistingService is { } service) UpdateService(service with { Name = name.Trim() });
        else
        {
            var topology = Graph.GetEnvironmentDefinition().Topology;
            if (topology.Nodes.Find(node => node.Id == _selection) is not { InfrastructureGroup: null } node) return;
            node.Name = name.Trim();
            Graph.SetTopology(topology);
        }
    }
    private void SetServiceEndpoint(string endpoint)
    {
        if (InfrastructureMode && Selected?.ExistingService is { } service) UpdateService(service with { Endpoint = endpoint.Trim() });
    }
    private void UpdateService(InfrastructureService service)
    {
        try
        {
            service.Validate(InfrastructureServiceTypes.Find(service.Type, Graph.ResourceAdapters));
            var topology = Graph.GetEnvironmentDefinition().Topology;
            var index = topology.Nodes.FindIndex(node => node.Id == _selection);
            if (index < 0 || topology.Nodes[index].InfrastructureGroup is not null) return;
            topology.Nodes[index] = topology.Nodes[index] with { Name = service.Name, ExistingService = service };
            Graph.SetTopology(topology);
        }
        catch (ArgumentException error) { Snackbar.Add(error.Message, Severity.Warning); }
    }
    private void RenameEnvironment(string name)
    {
        if (_disposed) return;
        try { Graph.RenameEnvironment(name); }
        catch (ArgumentException error) { Snackbar.Add(error.Message, Severity.Warning); }
    }
    private void SetHost(string id)
    {
        if (InfrastructureMode || _selection is null) return;
        var topology = Graph.GetEnvironmentDefinition().Topology;
        try
        {
            if (!topology.SetHost(_selection, string.IsNullOrEmpty(id) ? null : id)) return;
        }
        catch (ArgumentException error) { Snackbar.Add(error.Message, Severity.Warning); return; }

        var edited = topology.Nodes.Single(item => item.Id == _selection);
        if (edited.Host is { } hostId)
        {
            var nodes = _diagram.Nodes.OfType<TopologyNodeModel>();
            var model = nodes.Single(item => item.Definition.Id == edited.Id);
            var host = nodes.Single(item => item.Definition.Id == hostId);
            // Position against the destination before the incoming node contributes to legacy auto-sizing.
            var width = model.Size!.Width / model.DisplayScale;
            var height = model.Size.Height / model.DisplayScale;
            var offset = host.ConstrainChildOffset(32, 80, width, height);
            edited.X = host.Position.X + offset.X;
            edited.Y = host.Position.Y + offset.Y;
            if (host.Placement is null && host.Definition.Width.HasValue)
            {
                var destination = topology.Nodes.Single(item => item.Id == hostId);
                if (offset.X + width + 24 > host.Size!.Width) destination.Width = offset.X + width + 24;
                if (offset.Y + height + 24 > host.Size!.Height) destination.Height = offset.Y + height + 24;
            }
        }
        Graph.SetTopology(topology);
    }
    private void BindConnection(string path, TopologyBinding binding)
    {
        if (_selection is null) return;
        var topology = Graph.GetEnvironmentDefinition().Topology;
        var connections = Graph.Connections;
        try
        {
            if (topology.SetBinding(_selection, path, binding, connections.ConnectionType)) Graph.SetTopology(topology);
        }
        catch (ArgumentException error) { Snackbar.Add(error.Message, Severity.Warning); }
    }
    public void Dispose()
    {
        _disposed = true;
        _revealCheck = null;
        EndEdit();
        Graph.Changed -= Changed; Run.Changed -= RunChanged;
        Preferences.Changed -= Refresh;
        _diagram.SelectionChanged -= SelectionChanged; _diagram.PointerUp -= PointerUp;
        _diagram.ZoomChanged -= Refresh;
        _diagram.ContainerChanged -= RefreshPorts;
        if (!InfrastructureMode && Run.Checking) Run.CancelCheck();
    }
}
