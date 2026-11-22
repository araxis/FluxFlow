using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;

namespace FluxFlow.Studio.UI.Features.Designer;

public sealed partial class DesignerGraphState
{
    public long Revision { get; private set; }
    private int _manualEdits;
    private bool _applyingEdits;
    private void NotifyChanged() { if (!_applyingEdits) Changed?.Invoke(); }

    /// <summary>Hosts call editing methods on the same dispatcher as the canvas.</summary>
    public StudioEditProposal PrepareEdits(IEnumerable<ConfigurationEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        RequireIdleEditor();
        RecordEdit(); // Checkpoint any completed canvas edits before taking the proposal's revision.
        var batch = edits.ToArray();
        var candidate = _document.PrepareEdits(batch);
        foreach (var connect in batch.OfType<ConnectPortsEdit>())
        {
            CheckPort(connect.Source, output: true);
            CheckPort(connect.Target, output: false);
        }
        _ = candidate.Links(FindPresentation, DescribeNode);
        var json = candidate.ToJson();
        return new StudioEditProposal(this, Revision, json,
            Array.AsReadOnly(DesignerConfigurationValidator.Validate(json, _sectionPath, PluginValidators).ToArray()));

        void CheckPort(PortAddress address, bool output)
        {
            var component = address.ComponentAddress!;
            var node = candidate.Nodes(component.WorkflowName).Single(item => item.Address == component);
            var description = FindDescription(node.Type)
                ?? throw new InvalidOperationException($"No port description is available for '{node.Type}'.");
            var presentation = description.GetPresentation(node.Values);
            var ports = output ? presentation.Outputs : presentation.Inputs.Concat(presentation.SignalInputs);
            if (!ports.Any(port => port.Name == address.PortName))
                throw new ArgumentException($"'{address}' is not a described {(output ? "output" : "input")} port.");
        }
    }

    public bool ApplyEdits(StudioEditProposal proposal)
        => ApplyEdits(proposal, null);

    private bool ApplyEdits(StudioEditProposal proposal, Action? updateMetadata)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        RequireIdleEditor();
        RecordEdit(); // Also catches direct Inspector/canvas mutations awaiting their normal checkpoint.
        if (!ReferenceEquals(proposal.Owner, this) || proposal.Revision != Revision)
            throw new InvalidOperationException("The design changed; prepare the edits again.");
        if (proposal.Configuration == _document.ToJson()) return false;
        // Navigation is not an edit, so the latest checkpoint may describe another view.
        var before = _history[_historyIndex] with { Workflow = WorkflowName, SelectedNode = SelectedNode?.NodeName };
        var selectedLink = LinkSelection();
        _applyingEdits = true;
        try
        {
            var selected = SelectedNode?.NodeName;
            LoadDocument(proposal.Configuration, _sectionPath, WorkflowName, establishLayout: false);
            updateMetadata?.Invoke();
            if (selected is not null && Diagram.Nodes.OfType<Canvas.FlowNodeModel>().FirstOrDefault(node => node.NodeName == selected) is { } selection)
                Diagram.SelectModel(selection, true);
            RecordEdit();
            IsProjectOpen = true;
        }
        catch
        {
            Restore(before);
            SelectLink(selectedLink);
            throw;
        }
        finally { _applyingEdits = false; }
        NotifyChanged();
        return true;
    }

    /// <summary>Protects unfinished field edits, dialogs and gestures from proposal application.</summary>
    public IDisposable BeginManualEdit()
    {
        _manualEdits++;
        return new ManualEdit(this);
    }

    private void RequireIdleEditor()
    {
        if (_manualEdits != 0)
            throw new InvalidOperationException("Finish or cancel the current edit first.");
    }

    private sealed class ManualEdit(DesignerGraphState owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._manualEdits--;
        }
    }
}
