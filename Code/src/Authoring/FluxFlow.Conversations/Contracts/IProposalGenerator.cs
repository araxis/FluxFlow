using FluxFlow.Authoring;

namespace FluxFlow.Conversations;

/// <summary>Host-provided proposal generation; no design is modified by this operation.</summary>
public interface IProposalGenerator
{
    Task<ProposalResult> GenerateAsync(ProposalRequest request, CancellationToken cancellationToken = default);

    Task<ProposalResult> GenerateAsync(ProposalRequest request, Func<string, Task> onText,
        CancellationToken cancellationToken = default) => GenerateAsync(request, cancellationToken);
}

/// <summary>Display text or a completed response. Partial text never authorizes edits.</summary>
public sealed record ProposalUpdate(string? Text = null, ProposalResult? Result = null);

public sealed record ProposalRequest(string Instructions, string Configuration, string Workflow, string SectionPath = "",
    ProposalConnection? Connection = null)
{
    public bool Stepwise { get; init; }
    public string[] SelectedComponents { get; init; } = [];
    public string[] SelectedWorkflows { get; init; } = [];
    public TextAttachment[] Attachments { get; init; } = [];
    public ImageAttachment[] Images { get; init; } = [];
    public ProposalStep[] Steps { get; init; } = [];
    public ConversationTool[] Tools { get; init; } = [];
}

/// <summary>Completed editing tools and their outcome, supplied with a fresh configuration snapshot.</summary>
public sealed record ProposalStep(ConfigurationEdit[] Edits, string Outcome, ConversationToolCall? Tool = null);

public sealed record ConversationTool(string Name, string Description, System.Text.Json.JsonElement Schema);
public sealed record ConversationToolCall(string Name, System.Text.Json.JsonElement Arguments);

public sealed record ProposalResult
{
    public ConfigurationEdit[] Edits { get; init; } = [];
    public string? Error { get; init; }
    public string? Message { get; init; }
    public bool Continue { get; init; }
    public ConversationToolCall? Tool { get; init; }
}
