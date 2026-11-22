using FluxFlow.Authoring;

namespace FluxFlow.Conversations;

/// <summary>Host-owned editing and revision tracking. Implementations serialize calls on their editing dispatcher.</summary>
public interface IConversationWorkspace
{
    bool IsOpen { get; }
    long Revision { get; }
    ConversationSnapshot Capture();
    IConversationEdit Prepare(IEnumerable<ConfigurationEdit> edits);
    bool Apply(IConversationEdit edit);
    IReadOnlyList<ConversationTool> Tools => [];
    IConversationEdit Prepare(ConversationToolCall tool, string workflow) =>
        throw new InvalidOperationException("This host does not support the requested editing tool.");
}

public sealed record ConversationSnapshot(string Configuration, string Workflow, string SectionPath, long Revision);

public interface IConversationEdit
{
    long Revision { get; }
    string Configuration { get; }
    IReadOnlyList<ValidationMessageModel> Diagnostics { get; }
    string? Summary => null;
}
