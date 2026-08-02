using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using FluxFlow.Components.Designer.Persistence;
using FluxFlow.Composition.Addressing;
using FluxFlow.DesignerApp.Features.Designer.Canvas;
using FluxFlow.DesignerHost;

namespace FluxFlow.DesignerApp.Features.Designer;

/// <summary>
/// Owns the single <see cref="BlazorDiagram"/> for the designer canvas and the
/// current node selection, and bridges the canvas to the host-model persistence
/// mapping. The UI reads this state; it does not reach into the diagram directly.
/// <see cref="Changed"/> fires when the graph or selection changes.
/// </summary>
public sealed class DesignerGraphState
{
    public const string DefaultWorkflowName = "main";

    private readonly DesignerCatalog _catalog;
    private DesignerApplicationDocument _document = new()
    {
        Resources = new DesignerResourceNamespace { Path = "Resources" }
    };
    private HashSet<LinkEndpoints> _editableLoadedLinks = [];
    private int _added;

    public DesignerGraphState(DesignerCatalog catalog)
    {
        _catalog = catalog;
        Diagram = new BlazorDiagram();
        Diagram.SelectionChanged += OnSelectionChanged;
        Diagram.Links.Added += OnLinkAdded;
    }

    public BlazorDiagram Diagram { get; }

    public string WorkflowName { get; private set; } = DefaultWorkflowName;

    public FlowNodeModel? SelectedNode { get; private set; }

    public int NodeCount => Diagram.Nodes.Count;

    public bool HasSelection => Diagram.GetSelectedModels().Any();

    public event Action? Changed;

    /// <summary>Raised with a reason when a drawn link is rejected as invalid.</summary>
    public event Action<string>? LinkRejected;

    public FlowNodeModel AddNode(PaletteItemModel item)
    {
        _added++;
        var offset = 36 * (_added % 8);
        var node = new FlowNodeModel(MakeName(item), item, new Point(140 + offset, 80 + offset));
        Diagram.Nodes.Add(node);
        Changed?.Invoke();
        return node;
    }

    public void Clear()
    {
        Diagram.Links.Clear();
        Diagram.Nodes.Clear();
        _added = 0;
        _editableLoadedLinks.Clear();
        SelectedNode = null;
        Changed?.Invoke();
    }

    /// <summary>Remove the selected nodes and links. Removing a node also removes its links.</summary>
    public void DeleteSelected()
    {
        var selected = Diagram.GetSelectedModels().ToList();
        foreach (var link in selected.OfType<BaseLinkModel>())
        {
            Diagram.Links.Remove(link);
        }

        foreach (var node in selected.OfType<NodeModel>())
        {
            Diagram.Nodes.Remove(node);
        }

        SelectedNode = null;
        Changed?.Invoke();
    }

    private void OnLinkAdded(BaseLinkModel link) => link.TargetAttached += OnLinkTargetAttached;

    private void OnLinkTargetAttached(BaseLinkModel link)
    {
        if (IsValidConnection(link, out var reason))
        {
            Changed?.Invoke();
            return;
        }

        Diagram.Links.Remove(link);
        LinkRejected?.Invoke(reason);
    }

    private static bool IsValidConnection(BaseLinkModel link, out string reason)
    {
        reason = string.Empty;

        // Only enforce rules once both ends land on component ports.
        if (link.Source is not SinglePortAnchor source || source.Port.Parent is not FlowNodeModel sourceNode ||
            link.Target is not SinglePortAnchor target || target.Port.Parent is not FlowNodeModel targetNode)
        {
            return true;
        }

        if (ReferenceEquals(sourceNode, targetNode))
        {
            reason = "A node cannot connect to itself.";
            return false;
        }

        if (source.Port.Alignment != PortAlignment.Right ||
            target.Port.Alignment is not (PortAlignment.Left or PortAlignment.Top))
        {
            reason = "Links must go from an output port to a message or signal input port.";
            return false;
        }

        return true;
    }

    /// <summary>Serialize the current canvas as a canonical application definition.</summary>
    public string ToJson()
    {
        var canvas = DesignerGraphMapper.ToWorkflow(Diagram, WorkflowName);
        var components = new Dictionary<string, DesignerComponent>(
            canvas.Workflow.Components,
            StringComparer.Ordinal);
        if (_document.Workflows.TryGetValue(WorkflowName, out var previous))
        {
            foreach (var (name, component) in previous.Components)
            {
                if (_catalog.Find(component.Type) is null)
                    components.TryAdd(name, component);
            }
        }

        var workflows = new Dictionary<string, DesignerWorkflow>(
            _document.Workflows,
            StringComparer.Ordinal)
        {
            [WorkflowName] = canvas.Workflow with { Components = components }
        };
        var links = _document.Links
            .Where(link =>
                !DesignerGraphMapper.IsLocalWorkflowLink(link, WorkflowName) ||
                !_editableLoadedLinks.Contains(LinkEndpoints.From(link)))
            .Concat(canvas.Links)
            .ToArray();
        var edited = _document with
        {
            Workflows = workflows,
            Links = links
        };
        var json = _catalog.Persistence.Serialize(edited, writeIndented: true);
        _document = _catalog.Persistence.Load(json).Document;
        _editableLoadedLinks = canvas.Links
            .Select(LinkEndpoints.From)
            .ToHashSet();
        return json;
    }

    /// <summary>Rebuild the canvas from a canonical application definition.</summary>
    public IReadOnlyList<ValidationMessageModel> LoadJson(string json)
    {
        var loaded = _catalog.Persistence.Load(json);
        _document = loaded.Document;
        var workflow = loaded.Document.Workflows.TryGetValue(DefaultWorkflowName, out var preferred)
            ? preferred
            : loaded.Document.Workflows.Values.FirstOrDefault();

        if (workflow is null)
        {
            WorkflowName = DefaultWorkflowName;
            Clear();
            return ValidationMessageMapper.FromLinkDiagnostics(loaded.Diagnostics);
        }

        WorkflowName = workflow.Name;
        var relevantLinks = loaded.Document.Links.Where(link =>
            HasWorkflowEndpoint(link.Source, WorkflowName) ||
            HasWorkflowEndpoint(link.Target, WorkflowName));
        var messages = ValidationMessageMapper.FromLinkDiagnostics(loaded.Diagnostics)
            .Concat(DesignerGraphMapper.Load(Diagram, workflow, relevantLinks, _catalog.Find))
            .ToArray();
        _editableLoadedLinks = DesignerGraphMapper.ToWorkflow(Diagram, WorkflowName).Links
            .Select(LinkEndpoints.From)
            .ToHashSet();
        _added = Diagram.Nodes.Count;
        SelectedNode = null;
        Changed?.Invoke();
        return messages;
    }

    private static bool HasWorkflowEndpoint(ApplicationAddress address, string workflowName)
        => address.Kind == ApplicationAddressKind.WorkflowPort &&
           string.Equals(address.Segments[0], workflowName, StringComparison.Ordinal);

    private readonly record struct LinkEndpoints(string Source, string Target)
    {
        public static LinkEndpoints From(DesignerApplicationLink link)
            => new(link.Source.Value, link.Target.Value);
    }

    private void OnSelectionChanged(SelectableModel _)
    {
        SelectedNode = Diagram.GetSelectedModels().OfType<FlowNodeModel>().LastOrDefault();
        Changed?.Invoke();
    }

    private string MakeName(PaletteItemModel item)
    {
        var baseName = item.PreferredNodeName ?? item.ComponentType.Split('.')[^1];
        return $"{baseName}-{_added}";
    }
}
