
namespace FluxFlow.Conversations;

/// <summary>Pending preview state for one editing session; editing remains in the existing batch API.</summary>
public sealed class ConversationPreview(IConversationWorkspace graph, IProposalGenerator generator) : IDisposable
{
    private CancellationTokenSource? _generation;
    public IConversationEdit? Proposal { get; private set; }
    public string? Before { get; private set; }
    public string? Workflow { get; private set; }
    public bool IsGenerating => _generation is not null;
    public bool IsStale => Proposal is not null && Proposal.Revision != graph.Revision;

    public async Task<ProposalResult> GenerateAsync(string instructions, CancellationToken cancellationToken = default,
        ProposalConnection? connection = null, string? workflow = null, ProposalStep[]? steps = null,
        Func<string, Task>? onText = null, IReadOnlyList<string>? selectedComponents = null,
        IReadOnlyList<string>? selectedWorkflows = null, IReadOnlyList<TextAttachment>? attachments = null,
        IReadOnlyList<ImageAttachment>? images = null)
    {
        if (!graph.IsOpen)
            throw new InvalidOperationException("Open or create a project first.");
        if (Proposal is not null || IsGenerating)
            throw new InvalidOperationException("Apply or discard the current proposal first.");
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        var snapshot = graph.Capture();
        workflow ??= snapshot.Workflow;
        var request = new ProposalRequest(instructions, snapshot.Configuration, workflow, snapshot.SectionPath, connection)
        {
            Stepwise = steps is not null, Steps = steps ?? [],
            SelectedComponents = selectedComponents?.ToArray() ?? [], SelectedWorkflows = selectedWorkflows?.ToArray() ?? [],
            Attachments = attachments?.ToArray() ?? [], Images = images?.ToArray() ?? [], Tools = graph.Tools.ToArray()
        };
        TextAttachment.Validate(request.Attachments);
        ImageAttachment.Validate(request.Images);
        using var generation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _generation = generation;
        try
        {
            var result = onText is null
                ? await generator.GenerateAsync(request, generation.Token)
                : await generator.GenerateAsync(request, text =>
                    ReferenceEquals(_generation, generation) && !generation.IsCancellationRequested
                        ? onText(text) : Task.CompletedTask, generation.Token);
            generation.Token.ThrowIfCancellationRequested();
            if (result.Error is not null) throw new InvalidOperationException(result.Error);
            if (result.Tool is not null && (result.Edits is not { Length: 0 } ||
                !request.Tools.Any(tool => tool.Name == result.Tool.Name)))
                throw new InvalidOperationException("Request one supported editing tool at a time.");
            if (result.Edits is null || (result.Tool is null && result.Edits.Length == 0 &&
                (steps is null || result.Continue || string.IsNullOrWhiteSpace(result.Message))))
                throw new InvalidOperationException("No edits were proposed.");
            // Checkpoint direct field changes as well as ordinary edits, undo and document replacement.
            if (!graph.IsOpen || graph.Capture().Revision != snapshot.Revision)
                throw new InvalidOperationException("The design changed during generation; request a new proposal.");
            if (result.Tool is null && result.Edits.Length == 0) return result;
            var proposal = result.Tool is { } toolCall ? graph.Prepare(toolCall, workflow) : graph.Prepare(result.Edits);
            Before = snapshot.Configuration;
            Workflow = workflow;
            Proposal = proposal;
            return result with { Message = proposal.Summary ?? result.Message };
        }
        catch (Exception error) when (generation.IsCancellationRequested && error is not OperationCanceledException)
        {
            throw new OperationCanceledException("Proposal generation was cancelled.", error, generation.Token);
        }
        finally
        {
            if (ReferenceEquals(_generation, generation)) _generation = null;
        }
    }

    public bool Apply()
    {
        var proposal = Proposal ?? throw new InvalidOperationException("Prepare a proposal first.");
        var changed = graph.Apply(proposal);
        Discard();
        return changed;
    }

    public void Discard()
    {
        _generation?.Cancel();
        _generation = null;
        Proposal = null;
        Before = null;
        Workflow = null;
    }

    public void Dispose() => Discard();
}
