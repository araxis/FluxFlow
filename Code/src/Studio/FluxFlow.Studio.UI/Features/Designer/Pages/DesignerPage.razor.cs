using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models.Base;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Observations;
using FluxFlow.Studio.UI.Features.Designer.Components.Workspace;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using FluxFlow.Studio.UI.Features.Chat;
using FluxFlow.Studio.UI.Features.Designer.Projects;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace FluxFlow.Studio.UI.Features.Designer.Pages;

public partial class DesignerPage
{
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IStudioFileAccess Files { get; set; } = default!;
    [Inject] private Features.Observations.Validation.EditorValidationState EditorValidation { get; set; } = default!;
    private void RevealEditorDiagnostic() => _ = InvokeAsync(() => { _workspace.Show("Observations", DockPosition.Bottom); StateHasChanged(); });
    private NodeInspectorModel? _inspector;
    private readonly List<string> _openTabs = [];
    private bool _minimap = true, _openPrimaryEditor;
    [Inject] private CanvasViewState CanvasView { get; set; } = default!;
    private string? _studioPage;
    private Features.Plugins.PluginSettingsPage? _pluginSettingsPage;
    private Task Navigate(Action action) => Navigate(() => { action(); return Task.CompletedTask; });
    private async Task Navigate(Func<Task> action)
    {
        if (_studioPage == "Plugin settings" && _pluginSettingsPage is not null && !await _pluginSettingsPage.ConfirmLeaveAsync()) return;
        await action();
    }
    private bool _environmentOpen;
    private string? _connectionSelection;
    internal void OpenConnection(string path)
    {
        if (!Graph.Connections.Any(connection => connection.Path == path)) return;
        EndInspectorEdit();
        _environmentOpen = _environmentOpen && _environmentSection == "Topology";
        _studioPage = null;
        Graph.Diagram.UnselectAll();
        _connectionSelection = path;
        _workspace.Show("Inspector", DockPosition.Right);
    }
    internal async Task AddConnection()
    {
        EndInspectorEdit();
        using var edit = Graph.BeginManualEdit();
        var revision = Graph.Revision;
        var dialog = await DialogService.ShowAsync<Features.Connections.AddConnectionDialog>("Add connection",
            new DialogParameters<Features.Connections.AddConnectionDialog>
            { { d => d.Existing, Graph.Connections }, { d => d.Workflows, Graph.WorkflowNames }, { d => d.ActiveWorkflow, Graph.WorkflowName } });
        if (await dialog.Result is not { Canceled: false, Data: ConfigurationResource resource } || _disposed) return;
        if (revision != Graph.Revision) { Snackbar.Add("The project changed. Add the connection again.", Severity.Warning); return; }
        edit.Dispose();
        try { Graph.SetConnection(resource.Name, resource.Workflow, resource.Settings, add: true); OpenConnection(resource.Path); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { Snackbar.Add(error.Message, Severity.Error); }
    }
    private string _environmentSection = "Topology";
    private string? _serviceSetup;
    private void OpenServiceSetup(string path)
    {
        _serviceSetup = path;
        OpenEnvironment("Topology");
    }
    internal void SelectEnvironment(string id)
    {
        Graph.SelectEnvironment(id);
        OpenEnvironment("Topology");
    }
    internal async Task AddEnvironment()
    {
        var revision = Graph.Revision;
        var dialog = await DialogService.ShowAsync<Features.Environments.Topology.EnvironmentNameDialog>("Add environment");
        if (await dialog.Result is not { Canceled: false, Data: string name } || Graph.Revision != revision) return;
        try { Graph.AddEnvironment(name); OpenEnvironment("Topology"); }
        catch (ArgumentException error) { Snackbar.Add(error.Message, Severity.Warning); }
    }
    private int _environmentNavigation;
    private Features.Environments.EnvironmentPage? _environmentPage;
    private string? _infrastructureSelection;
    private bool _addInfrastructure;
    internal void OpenInfrastructure(string id)
    {
        if (!Preferences.Value.Environments.Infrastructure.Any(entry => entry.Id == id)) return;
        OpenEnvironment("Infrastructure");
        _infrastructureSelection = id;
    }
    private void AddInfrastructure()
    {
        OpenEnvironment("Infrastructure");
        _addInfrastructure = true;
    }
    private string? _runEnvironmentId;
    private long _runProjectGeneration;
    private string RunEnvironmentId => _runProjectGeneration == Graph.ProjectGeneration && Graph.Environments.Any(item => item.Id == _runEnvironmentId)
        ? _runEnvironmentId! : Graph.ActiveEnvironmentId;
    private void SelectRunEnvironment(string id)
    {
        Graph.SelectEnvironment(id);
        _runEnvironmentId = id;
        _runProjectGeneration = Graph.ProjectGeneration;
    }
    private FluxFlow.Environments.EnvironmentCheckResult? _topologyCheck;
    internal void OpenEnvironment(string section)
    {
        var samePage = section == "Infrastructure" && _environmentOpen && _environmentSection == section;
        _infrastructureSelection = null;
        _addInfrastructure = false;
        _connectionSelection = null;
        _studioPage = null;
        _environmentSection = section;
        if (!samePage) _environmentNavigation++;
        _environmentOpen = true;
    }
    private async Task StartRun()
    {
        if (RunSession.Checking || RunSession.Busy || (RunSession.HasRun && !RunSession.CanRetry) || !Graph.IsProjectOpen) return;
        Graph.SelectEnvironment(RunEnvironmentId);
        await RunSession.CheckAsync(Graph);
        if (RunSession.CheckRevision != Graph.Revision || RunSession.CheckResult is not { } check) return;
        if (!check.Success) { _topologyCheck = check; OpenEnvironment("Topology"); return; }
        _connectionSelection = null;
        _studioPage = null;
        _environmentOpen = false;
        SelectWorkflow(Graph.WorkflowName);
        _workspace.Show("Observations", DockPosition.Bottom);
        _applicationLogRequest = null;
        _logsRequest++;
        if (RunSession.CanRetry) await RunSession.RetryAsync(Graph);
        else await RunSession.StartAsync(Graph);
    }
    private int _logsRequest;
    private ApplicationTelemetryTarget? _applicationLogRequest;
    private int _tracesRequest;
    private ApplicationTelemetryTarget? _applicationTraceRequest;
    private void ShowApplicationObservations(ApplicationTelemetryTarget target, bool traces)
    {
        if (RunSession.ResolveApplicationTelemetry(Graph, target.InstanceId, target.RunId) is not { } current) return;
        if (traces) { _applicationTraceRequest = current; _tracesRequest++; }
        else { _applicationLogRequest = current; _logsRequest++; }
        _connectionSelection = null;
        _studioPage = null;
        _environmentOpen = false;
        _workspace.Show("Observations", DockPosition.Bottom);
    }
    private Task StopRun()
    {
        if (RunSession.Checking) { RunSession.CancelCheck(); return Task.CompletedTask; }
        return RunSession.StopAsync();
    }
    private ComponentActivity _activity = default!;
    private bool _disposed;
    private void RunChanged() => InvokeAsync(() =>
    {
        if (_disposed) return;
        _activity.Update(Graph, RunSession);
        StateHasChanged();
    });
    private IDisposable? _canvasEdit, _inspectorEdit;
    private void BeginCanvasEdit() { _connectionSelection = null; _environmentOpen = false; _canvasEdit ??= Graph.BeginManualEdit(); }
    private void EndCanvasEdit() { _canvasEdit?.Dispose(); _canvasEdit = null; }
    internal void BeginInspectorEdit()
    {
        // Connection fields commit through SetConnection, which owns its atomic edit.
        if (_connectionSelection is null) _inspectorEdit ??= Graph.BeginManualEdit();
    }
    private void EndInspectorEdit() { _inspectorEdit?.Dispose(); _inspectorEdit = null; }
    private void OpenStudioPage(string title)
    {
        _connectionSelection = null;
        _environmentOpen = false;
        _studioPage = title;
    }

    private void CloseStudioPage() => _studioPage = null;
    private DockWorkspace _workspace = default!;
    private Features.Environments.Topology.EnvironmentTopologyPage? _topologyPage;
    private bool TopologyVisible => _environmentOpen && _environmentSection == "Topology" && _connectionSelection is null;
    private ComponentInspector _componentInspector = default!;
    private ChatPanel? _chat;
    private readonly DockLayout _layout = new(() => new()
    {
        Percent = 70,
        First = new() { Horizontal = true, Percent = 22, First = new("Components"),
            Second = new() { Horizontal = true, Percent = 62, First = new("Canvas"), Second = new("Inspector") { Panels = { "Chat" } } } },
        Second = new("Observations")
    });
    private string? _dragType;
    private ObservationPanel _observations = default!;
    private IReadOnlyList<ValidationMessageModel>? _validation;
    private string? _validatedJson;
    private void OpenNodeEditor(FlowNodeModel node)
    {
        Graph.Diagram.SelectModel(node, true);
        _openPrimaryEditor = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_environmentOpen && _environmentSection == "Infrastructure" && _environmentPage is { } page)
        {
            if (_infrastructureSelection is { } entry)
            {
                _infrastructureSelection = null;
                page.SelectInfrastructure(entry);
            }
            if (_addInfrastructure)
            {
                _addInfrastructure = false;
                await page.AddInfrastructure();
            }
        }
        RevealTraceComponent();
        if (_topologyCheck is { } check)
        {
            _topologyCheck = null;
            if (TopologyVisible) _topologyPage?.ShowCheckResult(check);
        }
        if (_serviceSetup is { } path)
        {
            _serviceSetup = null;
            if (TopologyVisible && _topologyPage?.ShowServiceSetup(path) != true)
                Snackbar.Add("Place this connection from the Services palette to configure it in this environment.", Severity.Info);
        }
        if (!_openPrimaryEditor) return;
        _openPrimaryEditor = false;
        await _componentInspector.OpenPrimaryEditorAsync();
    }

    protected override void OnInitialized()
    {
        _runEnvironmentId = Graph.ActiveEnvironmentId;
        _runProjectGeneration = Graph.ProjectGeneration;
        _activity = new(RunSession);
        Graph.RecordEdit();
        if (Graph.IsProjectOpen) _openTabs.AddRange(Graph.WorkflowNames);
        _inspector = Graph.SelectedNode is { } node ? node.Description.GetInspector(node.Configuration) : null;
        Graph.Changed += OnGraphChanged;
        RunSession.Changed += RunChanged;
        Graph.LinkRejected += OnLinkRejected;
        Graph.Diagram.PointerUp += CanvasPointerUp;
        Graph.Diagram.ZoomChanged += OnZoomChanged;
        Preferences.Changed += OnPreferencesChanged;
        EditorValidation.RevealRequested += RevealEditorDiagnostic;
    }

    private void OnPreferencesChanged() => _ = InvokeAsync(StateHasChanged);

    private void OnZoomChanged() => InvokeAsync(StateHasChanged);
    private void OnGraphChanged()
    {
        if (_runProjectGeneration != Graph.ProjectGeneration || Graph.HasSelection ||
            (_connectionSelection is not null && !Graph.Connections.Any(connection => connection.Path == _connectionSelection)))
            _connectionSelection = null;
        if (_runProjectGeneration != Graph.ProjectGeneration || !Graph.Environments.Any(item => item.Id == _runEnvironmentId))
        {
            _runEnvironmentId = Graph.ActiveEnvironmentId;
            _runProjectGeneration = Graph.ProjectGeneration;
        }
        _activity.Update(Graph, RunSession);
        if (RunSession.Checking && RunSession.CheckRevision != Graph.Revision) RunSession.CancelCheck();
        _inspector = Graph.SelectedNode is { } node ? node.Description.GetInspector(node.Configuration) : null;
        _openTabs.RemoveAll(name => !Graph.WorkflowNames.Contains(name));
        InvokeAsync(StateHasChanged);
    }
    internal void SelectWorkflow(string name)
    {
        _studioPage = null;
        _connectionSelection = null;
        _environmentOpen = false;
        Graph.IsProjectOpen = true;
        if (!_openTabs.Contains(name)) _openTabs.Add(name);
        Graph.SelectWorkflow(name);
    }
    private void CloseTab(string name)
    {
        // Closing a tab never deletes its workflow from configuration.
        _openTabs.Remove(name);
        if (Graph.WorkflowName == name && _openTabs.Count > 0) Graph.SelectWorkflow(_openTabs[^1]);
    }
    private async Task AddWorkflow()
    {
        var dialog = await DialogService.ShowAsync<WorkflowNameDialog>("Add workflow",
            new DialogParameters<WorkflowNameDialog> { { d => d.Existing, Graph.WorkflowNames } });
        var result = await dialog.Result;
        if (result is not { Canceled: false, Data: string name }) return;
        Graph.AddWorkflow(name);
        Graph.IsProjectOpen = true;
        SelectWorkflow(name);
        RecordEdit();
    }
    private void AddNode(string type) => AddNode(type, null);
    private void AddNode(string type, Point? position)
    {
        if (Graph.FindDescription(type) is not { } item) return;
        Graph.IsProjectOpen = true;
        if (!_openTabs.Contains(Graph.WorkflowName)) _openTabs.Add(Graph.WorkflowName);
        var node = Graph.AddNode(item, position);
        Graph.Diagram.SelectModel(node, true);
        RecordEdit();
    }
    private void DropComponent(DragEventArgs e)
    {
        if (_dragType is not { } type) return;
        _dragType = null;
        AddNode(type, Graph.Diagram.GetRelativeMousePoint(e.ClientX, e.ClientY));
    }
    private async void CanvasPointerUp(Model? model, Blazor.Diagrams.Core.Events.PointerEventArgs args)
    {
        // Let the native diagram finish its drag/link operation before taking an editor checkpoint.
        await InvokeAsync(async () =>
        {
            await Task.Yield();
            try { RecordEdit(); }
            finally { EndCanvasEdit(); }
        });
    }
    private void CanvasKeyUp(KeyboardEventArgs args)
    {
        if (args.CtrlKey && args.Key.Equals("z", StringComparison.OrdinalIgnoreCase)) Undo();
        else if (args.CtrlKey && args.Key.Equals("y", StringComparison.OrdinalIgnoreCase)) Redo();
        else if (args.Key is "Delete" or "Backspace") RecordEdit();
    }
    private void RecordEdit() { Graph.RecordEdit(); InvalidateValidation(); }
    private void Undo() => ApplyHistory(Graph.Undo);
    private void Redo() => ApplyHistory(Graph.Redo);
    private void ApplyHistory(Action apply)
    {
        var connection = _connectionSelection;
        apply();
        // Graph history restores canvas selection; keep the active resource Inspector
        // when its connection still exists in the restored configuration.
        if (connection is not null) OpenConnection(connection);
        EnsureWorkflowTab();
        InvalidateValidation();
    }
    private void EnsureWorkflowTab() { if (!_openTabs.Contains(Graph.WorkflowName)) _openTabs.Add(Graph.WorkflowName); }
    private void InvalidateValidation()
    {
        if (_validation is not null && _validatedJson != Graph.ToJson()) _validation = null;
    }
    private void Validate()
    {
        _validatedJson = Graph.ToJson();
        _validation = DesignerConfigurationValidator.Validate(_validatedJson, Graph.SectionPath, Graph.PluginValidators);
        _workspace.Show("Observations", DockPosition.Bottom);
        _observations.ShowValidation();
    }
    private void NavigateDiagnostic(ValidationMessageModel diagnostic)
    {
        if (diagnostic.Address is not { } address) return;
        SelectWorkflow(address.WorkflowName);
        var node = Graph.Diagram.Nodes.OfType<FlowNodeModel>().FirstOrDefault(item => item.NodeName == address.ComponentName);
        if (node is null) { Snackbar.Add("This component has no editor here. Its configuration is preserved.", Severity.Info); return; }
        Graph.Diagram.SelectModel(node, true);
        _workspace.Show("Inspector", DockPosition.Right);
    }
    private void NavigateTrace(FluxFlow.Environments.ExecutionTrace span)
    {
        if (!span.Attributes.ContainsKey("flow.component.address")) return;
        var address = RunSession.ResolveTraceComponent(Graph, span);
        if (address is null || !Graph.WorkflowNames.Contains(address.WorkflowName))
        {
            Snackbar.Add("This span has no current component, or the project has changed since this run. Its recorded details remain available.", Severity.Info);
            return;
        }
        SelectWorkflow(address.WorkflowName);
        var node = Graph.Diagram.Nodes.OfType<FlowNodeModel>().FirstOrDefault(item => item.NodeName == address.ComponentName);
        if (node is null) { Snackbar.Add("This recorded component is not available on the canvas.", Severity.Info); return; }
        Graph.Diagram.SelectModel(node, true);
        _workspace.Show("Canvas", DockPosition.Left);
        _workspace.Show("Inspector", DockPosition.Right);
        _traceFocus = node;
    }
    private FlowNodeModel? _traceFocus;
    internal void RevealTraceComponent()
    {
        var node = _traceFocus;
        _traceFocus = null;
        if (node is null || !node.Selected || !Graph.Diagram.Nodes.Contains(node)) return;
        if (Graph.Diagram.Container is { Width: > 0, Height: > 0 } viewport)
        {
            var zoom = Graph.Diagram.Zoom;
            Graph.Diagram.SetPan(viewport.Width / 2 - (node.Position.X + (node.Size?.Width ?? 0) / 2) * zoom,
                viewport.Height / 2 - (node.Position.Y + (node.Size?.Height ?? 0) / 2) * zoom);
        }
        node.TraceFocusVersion++;
        node.Refresh();
    }
    private void DeleteSelected() { Graph.DeleteSelected(); RecordEdit(); }
    private async Task Clear()
    {
        if (!await ShowMessage("Clear rendered components?", "Known links to these components will also be removed. Unknown components, resources and other workflows will remain. Custom references may need correction.", "Clear")) return;
        Graph.Clear(); RecordEdit();
    }
    private void Zoom(double factor) => Graph.Diagram.SetZoom(Math.Clamp(Graph.Diagram.Zoom * factor, .2, 2));
    private async Task OpenSettings(StudioTheme theme, bool project)
    {
        if (!project) { OpenStudioPage("Studio settings"); return; }
        await DialogService.ShowAsync<StudioSettingsDialog>("Project settings",
            new DialogParameters<StudioSettingsDialog>
            {
                { d => d.ProjectColors, Graph.PortColorOverrides },
                { d => d.Saved, EventCallback.Factory.Create<PortColors>(this, Graph.SetPortColorOverrides) },
                { d => d.Defaults, Preferences.Value.PortColors.Resolve(theme.DefaultPortColors) }
            }, new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
    }
    private bool _publishing;
    private async Task ExportApplication(bool compiled)
    {
        if (_publishing || !Graph.IsProjectOpen) return;
        _publishing = true;
        try
        {
            if (compiled) await ChooseTargetAndPublish();
            else await Files.GenerateApplicationSourceAsync(await Graph.ExportCompiledConfigurationAsync(RunEnvironmentId));
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception exception) { Snackbar.Add(exception.Message, Severity.Error); }
        finally { _publishing = false; }
    }

    private async Task ChooseTargetAndPublish()
    {
        int target = 11;
        string? failure = null;
        while (!_disposed && Graph.IsProjectOpen)
        {
            var dialog = await DialogService.ShowAsync<CompiledPublishDialog>("Publish compiled application",
                new DialogParameters<CompiledPublishDialog> { { d => d.Target, target }, { d => d.Failure, failure } },
                new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true });
            if (await dialog.Result is not { Canceled: false, Data: int selected } || _disposed || !Graph.IsProjectOpen) return;
            target = selected;
            try
            {
                var configuration = await Graph.ExportCompiledConfigurationAsync(RunEnvironmentId);
                await Files.PublishCompiledApplicationAsync(configuration, target);
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error) { failure = error.Message; }
        }
    }

    private async Task PublishApplication()
    {
        if (_publishing || !Graph.IsProjectOpen) return;
        _publishing = true;
        try
        {
            var environment = Graph.Environments.Single(e => e.Id == RunEnvironmentId);
            var service = environment.Setup.Resources.FirstOrDefault(r => r.Value.Provider != "existing");
            if (service.Key is not null)
                throw new InvalidOperationException($"'{service.Key}' requires environment provisioning. Publishing currently supports manual connections.");
            var apps = environment.Topology.Nodes.Where(n => n.Kind == FluxFlow.Environments.EnvironmentNodeKind.Application).ToArray();
            if (apps.Length > 1 || apps.Any(a => a.Bindings.Count > 0))
                throw new InvalidOperationException("Publishing supports a single application with manual connections. Choose a Run environment with that setup.");
            await Files.PublishApplicationAsync(Path.ChangeExtension(Graph.ProjectFile.Name, ".zip"), Graph.ExportConfiguration(), environment.Setup.StartupTimeoutSeconds);
        }
        catch (Exception exception) { Snackbar.Add($"Could not publish application: {exception.Message}", Severity.Error); }
        finally { _publishing = false; }
    }

    private async Task Save(bool project, bool saveAs = false)
    {
        try
        {
            var file = project ? Graph.ProjectFile with { Content = Graph.ToProjectJson() }
                : new StudioFile("application.json", Graph.ExportConfiguration());
            var saved = await Files.SaveAsync(file, saveAs || !project);
            if (project && saved is not null) Graph.ProjectFile = saved;
        }
        catch (Exception exception) { Snackbar.Add($"Could not save file: {exception.Message}", Severity.Error); }
    }
    internal async Task Load(bool project)
    {
        try
        {
            StudioFile? file;
            var sectionPath = "";
            if (project) file = await Files.OpenAsync(".fluxflow");
            else
            {
                var dialog = await DialogService.ShowAsync<GraphJsonDialog>("Import engine configuration",
                    new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
                if (await dialog.Result is not { Canceled: false, Data: ValueTuple<string, string, string> input }) return;
                file = new StudioFile(input.Item3, input.Item1);
                sectionPath = input.Item2;
            }
            if (file is null || !await ConfirmReplace()) return;
            if (!project && JsonNode.Parse(file.Content)?["Format"]?.ToString() == "FluxFlow.Studio")
                throw new ArgumentException("This is a Studio project. Use Open project instead of Import.");
            var messages = project ? Graph.LoadProject(file.Content) : Graph.LoadJson(file.Content, sectionPath);
            // LoadProject restores environment metadata after loading the workflow graph.
            _runEnvironmentId = Graph.ActiveEnvironmentId;
            _chat?.Clear();
            Graph.ProjectFile = project ? file : new StudioFile(System.IO.Path.GetFileNameWithoutExtension(file.Name) + ".fluxflow", "");
            Graph.IsProjectOpen = true;
            CloseStudioPage();
            _openTabs.Clear(); _openTabs.AddRange(Graph.WorkflowNames);
            Graph.ResetHistory();
            _validation = null;
            foreach (var message in messages) Snackbar.Add(message.Message, Severity.Warning);
        }
        catch (Exception exception) { Snackbar.Add($"Could not load {(project ? "project" : "configuration")}: {exception.Message}", Severity.Error); }
    }
    private Task<bool> ConfirmReplace() => ShowMessage("Replace current editor contents?", "Save your Studio project to keep configuration, layout and overrides. Files on disk are not changed.", "Replace");
    private async Task NewProject() { if (await ConfirmReplace()) ResetProject(true); }
    private async Task CloseProject() { if (await ConfirmReplace()) ResetProject(false); }
    private void ResetProject(bool open)
    {
        _chat?.Clear();
        CloseStudioPage();
        Graph.IsProjectOpen = open;
        Graph.ProjectFile = new("application.fluxflow", "");
        Graph.LoadJson("{\"Workflows\":{\"main\":{}}}"); Graph.ResetHistory();
        _validation = null;
        _openTabs.Clear(); if (open) _openTabs.Add("main");
    }
    private Task ShowHelp() => ShowMessage("FluxFlow Studio", "Open/Save works with Studio project files, including configuration, layout and project overrides. Import/Export works with engine JSON only. Studio settings are under Edit; project settings are under File. Use Environment for resource setup, container images and local runs. Double-click a component to open its primary editor. Closing a tab keeps its workflow. Drag panel titles to dock; resize dividers with the pointer or arrow keys. Restore panels from View. Live debugging remains separate work.");
    private async Task<bool> ShowMessage(string title, string message, string? action = null)
    {
        var dialog = await DialogService.ShowAsync<StudioMessageDialog>(title,
            new DialogParameters<StudioMessageDialog> { { d => d.Message, message }, { d => d.Action, action } });
        return await dialog.Result is { Canceled: false, Data: true };
    }
    private void OnLinkRejected(string reason) => Snackbar.Add(reason, Severity.Warning);
    public void Dispose()
    {
        _disposed = true;
        _activity.Clear();
        RunSession.CancelCheck();
        EndCanvasEdit();
        EndInspectorEdit();
        Graph.Changed -= OnGraphChanged;
        RunSession.Changed -= RunChanged;
        Graph.LinkRejected -= OnLinkRejected;
        Graph.Diagram.PointerUp -= CanvasPointerUp;
        Graph.Diagram.ZoomChanged -= OnZoomChanged;
        Preferences.Changed -= OnPreferencesChanged;
        EditorValidation.RevealRequested -= RevealEditorDiagnostic;
    }
}
