using FluxFlow.Conversations;
using FluxFlow.Authoring;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using FluxFlow.Application.Addressing;
using System.Text.Json;

namespace FluxFlow.Studio.UI.Features.Chat;

public sealed class StudioConversationWorkspace(DesignerGraphState graph) : IConversationWorkspace
{
    private static readonly IReadOnlyList<ConversationTool> ColorTools = Array.AsReadOnly(new[]
    {
        new ConversationTool("set_component_color", "Change a component's Studio color.", JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","additionalProperties":false,"required":["Address","Color"],"properties":{
              "Address":{"type":"string","description":"Existing component address in the active workflow, such as main.scheduler-1."},
              "Color":{"type":["string","null"],"description":"Hex color such as #0000FF for blue; null resets the component's default color."}
            }}
            """))
    });
    public IReadOnlyList<ConversationTool> Tools => ColorTools;
    public bool IsOpen => graph.IsProjectOpen;
    public long Revision => graph.Revision;

    public ConversationSnapshot Capture()
    {
        var snapshot = graph.PrepareEdits([]);
        return new(snapshot.Configuration, graph.WorkflowName, graph.SectionPath, snapshot.Revision);
    }

    public IConversationEdit Prepare(IEnumerable<ConfigurationEdit> edits) => new Edit(graph.PrepareEdits(edits));
    public IConversationEdit Prepare(ConversationToolCall tool, string workflow)
    {
        if (tool.Name != "set_component_color") throw new ArgumentException("Unsupported Studio editing tool.");
        var arguments = tool.Arguments;
        if (arguments.ValueKind != JsonValueKind.Object || arguments.EnumerateObject().Count() != 2 ||
            !arguments.TryGetProperty("Address", out var target) || target.ValueKind != JsonValueKind.String ||
            !arguments.TryGetProperty("Color", out var value) || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            throw new ArgumentException("Supply a component Address and a hexadecimal Color, or null to reset it.");
        var address = ComponentAddress.Parse(target.GetString()!);
        var color = value.GetString();
        PortColors.ValidateColor(color);
        var snapshot = Capture();
        if (address.WorkflowName != workflow || !new DesignerConfiguration(snapshot.Configuration, snapshot.SectionPath)
            .Nodes(workflow).Any(node => node.Address == address))
            throw new ArgumentException("Color changes must target an existing component in the request's active workflow.");
        return new ColorEdit(this, snapshot.Revision, snapshot.Configuration, address, color);
    }

    public bool Apply(IConversationEdit edit)
    {
        if (edit is not ColorEdit color) return graph.ApplyEdits(((Edit)edit).Proposal);
        graph.RecordEdit();
        if (!ReferenceEquals(color.Owner, this) || color.Revision != graph.Revision || !graph.IsProjectOpen)
            throw new InvalidOperationException("The design changed; request a new proposal.");
        return graph.SetNodeColor(color.Address, color.Color);
    }

    private sealed record ColorEdit(StudioConversationWorkspace Owner, long Revision, string Configuration,
        ComponentAddress Address, string? Color) : IConversationEdit
    {
        public IReadOnlyList<ValidationMessageModel> Diagnostics => [];
        public string Summary => Color is null ? $"Reset {Address.Value} to its default color." : $"Set {Address.Value} color to {Color}.";
    }

    private sealed class Edit(StudioEditProposal proposal) : IConversationEdit
    {
        internal StudioEditProposal Proposal { get; } = proposal;
        public long Revision => Proposal.Revision;
        public string Configuration => Proposal.Configuration;
        public IReadOnlyList<ValidationMessageModel> Diagnostics => Proposal.Diagnostics;
    }
}
