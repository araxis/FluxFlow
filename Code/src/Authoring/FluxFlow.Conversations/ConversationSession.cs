namespace FluxFlow.Conversations;

public enum ChatEditingMode { Ask, Automatic }

/// <summary>One conversation. Editing, approval and history are owned by the calling host.</summary>
public sealed class ConversationSession(IConversationWorkspace workspace, IProposalGenerator generator) : IDisposable
{
    public sealed class Turn(string request, string workflow, IReadOnlyList<string>? selectedComponents = null,
        IReadOnlyList<string>? selectedWorkflows = null, IReadOnlyList<TextAttachment>? attachments = null,
        IReadOnlyList<ImageAttachment>? images = null)
    {
        public string Request { get; } = request;
        public string Workflow { get; } = workflow;
        public IReadOnlyList<string> SelectedComponents { get; } = Array.AsReadOnly(selectedComponents?.ToArray() ?? []);
        public IReadOnlyList<string> SelectedWorkflows { get; } = Array.AsReadOnly(selectedWorkflows?.ToArray() ?? []);
        public IReadOnlyList<TextAttachment> Attachments { get; } = Array.AsReadOnly(attachments?.ToArray() ?? []);
        public IReadOnlyList<ImageAttachment> Images { get; } = Array.AsReadOnly(images?.ToArray() ?? []);
        public string Response { get; internal set; } = "Preparing changes…";
        public List<Step> Steps { get; } = [];
    }

    public sealed class Step(ProposalResult result)
    {
        public ProposalResult Result { get; } = result;
        public string Outcome { get; internal set; } = "Waiting for approval.";
    }

    private sealed class Request(Turn turn, string instructions, ProposalConnection? connection, ChatEditingMode mode, long revision)
    {
        public Turn Turn { get; } = turn;
        public string Instructions { get; } = instructions;
        public ProposalConnection? Connection { get; } = connection;
        public ChatEditingMode Mode { get; } = mode;
        public long Revision { get; set; } = revision;
    }

    private readonly List<Turn> _turns = [];
    private Request? _active;
    private bool _disposed;
    public IReadOnlyList<Turn> Turns => _turns;
    public ConversationPreview Preview { get; } = new(workspace, generator);
    public bool IsBusy => _active is not null;
    public string? ActiveWorkflow => _active?.Turn.Workflow;
    public event Action? Changed;
    public event Action? Applied;

    public async Task SendAsync(string request, ProposalConnection? connection, ChatEditingMode mode = ChatEditingMode.Ask,
        IReadOnlyList<string>? selectedComponents = null, IReadOnlyList<string>? selectedWorkflows = null,
        IReadOnlyList<TextAttachment>? attachments = null, IReadOnlyList<ImageAttachment>? images = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        if (IsBusy || Preview.Proposal is not null)
            throw new InvalidOperationException("Apply or discard the current proposal first.");
        if (!workspace.IsOpen) throw new InvalidOperationException("Open or create a project first.");
        if (request.Length > 8000) throw new ArgumentException("Keep the request within 8,000 characters.");
        var snapshot = workspace.Capture();
        var attached = selectedComponents?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var workflows = selectedWorkflows?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var files = attachments?.ToArray() ?? [];
        TextAttachment.Validate(files);
        var pictures = images?.ToArray() ?? [];
        ImageAttachment.Validate(pictures);
        var document = new FluxFlow.Authoring.DesignerConfiguration(snapshot.Configuration, snapshot.SectionPath);
        if (workflows.Any(name => !document.WorkflowNames.Contains(name, StringComparer.Ordinal)))
            throw new ArgumentException("An attached workflow is no longer available. Remove it or attach a workflow again.");
        var nodes = document.Nodes(snapshot.Workflow);
        if (attached.Any(address => !nodes.Any(node => node.Address.Value == address)))
            throw new ArgumentException("The attached component is no longer in the active workflow. Remove it or attach a component again.");
        var instructions = request;
        foreach (var previous in _turns.AsEnumerable().Reverse())
        {
            var context = $"Previous request ({previous.Workflow}): {previous.Request}\nAttached components: {string.Join(", ", previous.SelectedComponents)}\nAttached workflows: {string.Join(", ", previous.SelectedWorkflows)}\nOutcome: {previous.Response}\n\n";
            if (context.Length + instructions.Length > 8000) break;
            instructions = context + instructions;
        }
        var turn = new Turn(request, snapshot.Workflow, attached, workflows, files, pictures);
        _turns.Add(turn);
        var active = new Request(turn, instructions, connection, mode, snapshot.Revision);
        _active = active;
        await AdvanceAsync(active);
    }

    private async Task AdvanceAsync(Request active)
    {
        try
        {
            while (ReferenceEquals(_active, active))
            {
                if (active.Turn.Steps.Count >= 16)
                {
                    active.Turn.Response = "Stopped after 16 editing steps. Review the design before continuing.";
                    _active = null;
                    break;
                }
                if (!workspace.IsOpen || workspace.Capture().Revision != active.Revision)
                    throw new InvalidOperationException("The design changed; send a new request to continue.");
                active.Turn.Response = "Responding…";
                var receivedText = false;
                var generating = Preview.GenerateAsync(active.Instructions, connection: active.Connection,
                    workflow: active.Turn.Workflow, selectedComponents: active.Turn.SelectedComponents,
                    selectedWorkflows: active.Turn.SelectedWorkflows,
                    attachments: active.Turn.Attachments,
                    images: active.Turn.Images,
                    steps: active.Turn.Steps.Select(step => new ProposalStep(step.Result.Edits, step.Outcome, step.Result.Tool)).ToArray(),
                    onText: text =>
                    {
                        if (!ReferenceEquals(_active, active)) return Task.CompletedTask;
                        active.Turn.Response = receivedText ? active.Turn.Response + text : text;
                        receivedText = true;
                        Changed?.Invoke();
                        return Task.CompletedTask;
                    });
                Changed?.Invoke();
                var result = await generating;
                if (!ReferenceEquals(_active, active)) return;
                if (Preview.Proposal is null)
                {
                    active.Turn.Response = result.Message!;
                    _active = null;
                    break;
                }
                active.Turn.Steps.Add(new(result));
                active.Turn.Response = result.Message ?? $"Changes for {active.Turn.Workflow} are ready to review.";
                Changed?.Invoke();
                if (active.Mode == ChatEditingMode.Ask) break;
                await Task.Yield();
                if (!ReferenceEquals(_active, active)) return;
                if (!ApplyStep(active)) break;
            }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_active, active))
            { active.Turn.Response = "Request cancelled. Completed changes are retained."; _active = null; }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.Text.Json.JsonException or FormatException or HttpRequestException)
        {
            if (ReferenceEquals(_active, active))
            {
                active.Turn.Response = error.Message;
                if (Preview.Proposal is null) _active = null;
            }
        }
        finally { Changed?.Invoke(); }
    }

    private bool ApplyStep(Request active)
    {
        var changed = Preview.Apply();
        var step = active.Turn.Steps[^1];
        step.Outcome = changed ? "Applied." : "No changes needed.";
        active.Revision = workspace.Revision;
        active.Turn.Response = changed ? "Changes applied." : "The design is unchanged.";
        if (changed) Applied?.Invoke();
        var more = changed && step.Result.Continue;
        if (!more) _active = null;
        Changed?.Invoke();
        return more;
    }

    public async Task ApplyAsync()
    {
        var active = _active ?? throw new InvalidOperationException("Prepare a proposal first.");
        if (active.Mode != ChatEditingMode.Ask) throw new InvalidOperationException("Automatic editing is already applying this request.");
        if (ApplyStep(active)) await AdvanceAsync(active);
    }

    public void Discard()
    {
        var active = _active;
        _active = null;
        if (active is not null)
        {
            active.Turn.Response = Preview.Proposal is null
                ? "Request cancelled. Completed changes are retained."
                : "Proposal discarded. Completed changes are retained.";
            if (Preview.Proposal is not null && active.Turn.Steps.LastOrDefault() is { } step)
                step.Outcome = "Discarded.";
        }
        Preview.Discard();
        Changed?.Invoke();
    }

    public void Clear() { Discard(); _turns.Clear(); Changed?.Invoke(); }
    public void Dispose() { _disposed = true; Discard(); }
}
