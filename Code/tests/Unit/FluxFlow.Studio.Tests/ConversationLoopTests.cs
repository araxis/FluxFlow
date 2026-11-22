using System.Threading.Channels;
using FluxFlow.Studio.UI.Features.Designer;
using Shouldly;
using Xunit;
using static FluxFlow.Studio.Tests.ConfigurationEditingTests;

namespace FluxFlow.Studio.Tests;

public sealed class ConversationLoopTests
{
    [Theory]
    [InlineData(ChatEditingMode.Ask)]
    [InlineData(ChatEditingMode.Automatic)]
    public async Task Color_tools_use_approval_and_one_undo_step_without_changing_configuration(ChatEditingMode mode)
    {
        var graph = Create();
        var before = graph.ToProjectJson();
        var configuration = graph.ToJson();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var sending = chat.SendAsync("Make the component blue", null, mode, ["main.source"]);
        var first = await generator.Next();
        first.Request.Tools.Single().Name.ShouldBe("set_component_color");
        first.Complete(ColorChange(continuing: true));
        if (mode == ChatEditingMode.Ask)
        {
            await sending;
            graph.ToProjectJson().ShouldBe(before);
            var summary = chat.Preview.Proposal!.Summary.ShouldNotBeNull();
            summary.ShouldContain("main.source");
            summary.ShouldContain("#0000FF");
            sending = chat.ApplyAsync();
        }
        var next = await generator.Next();
        next.Request.Steps.Single().Tool!.Name.ShouldBe("set_component_color");
        next.Request.Steps.Single().Outcome.ShouldBe("Applied.");
        next.Complete(new() { Message = "The component is blue." });
        await sending;
        graph.ToJson().ShouldBe(configuration);
        var applied = graph.ToProjectJson();
        applied.ShouldNotBe(before);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(applied);
        var workspace = new StudioConversationWorkspace(graph);
        workspace.Apply(workspace.Prepare(ColorChange().Tool!, "main")).ShouldBeFalse();
        workspace.Apply(workspace.Prepare(ColorChange(color: null).Tool!, "main")).ShouldBeTrue();
        graph.ToProjectJson().ShouldBe(before);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(applied);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("approval")]
    [InlineData("automatic")]
    [InlineData("automatic-cancel")]
    [InlineData("cancel")]
    [InlineData("discard")]
    public async Task Color_tools_reject_stale_and_cancelled_changes(string stage)
    {
        var graph = Create();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var interrupted = false;
        if (stage is "automatic" or "automatic-cancel") chat.Changed += () =>
        {
            if (interrupted || chat.Preview.Proposal is null) return;
            interrupted = true;
            if (stage == "automatic-cancel") chat.Discard();
            else graph.SetNodeColor(Address("main.source"), "#FF0000");
        };
        var sending = chat.SendAsync("Make it blue", null, stage is "automatic" or "automatic-cancel" ? ChatEditingMode.Automatic : ChatEditingMode.Ask);
        var call = await generator.Next();
        if (stage == "pending") graph.SetNodeColor(Address("main.source"), "#FF0000");
        if (stage == "cancel") chat.Discard();
        call.Complete(ColorChange());
        await sending;
        if (stage == "approval")
        {
            graph.SetNodeColor(Address("main.source"), "#FF0000");
            graph.Undo(); // Returning to the old appearance must still invalidate approval.
            chat.Preview.IsStale.ShouldBeTrue();
            await Should.ThrowAsync<InvalidOperationException>(() => chat.ApplyAsync());
        }
        if (stage == "discard") chat.Discard();
        graph.ToProjectJson().ShouldNotContain("#0000FF");
        if (stage is "pending" or "automatic") graph.ToProjectJson().ShouldContain("#FF0000");
    }

    [Theory]
    [InlineData(ChatEditingMode.Ask)]
    [InlineData(ChatEditingMode.Automatic)]
    public async Task Color_reset_keeps_the_captured_target_after_tab_navigation_and_is_undoable(ChatEditingMode mode)
    {
        var graph = Create();
        graph.SetNodeColor(Address("main.source"), "#FF0000");
        graph.SetNodeColor(Address("other.sink"), "#00FF00");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ToJson();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var sending = chat.SendAsync("Reset its color", null, mode, ["main.source"]);
        var call = await generator.Next();
        graph.SelectWorkflow("other");
        var pending = graph.ToProjectJson();
        call.Complete(ColorChange(color: null));
        await sending;
        if (mode == ChatEditingMode.Ask)
        {
            graph.ToProjectJson().ShouldBe(pending);
            chat.Preview.Proposal!.Summary.ShouldBe("Reset main.source to its default color.");
            await chat.ApplyAsync();
        }
        graph.WorkflowName.ShouldBe("other");
        graph.ToJson().ShouldBe(configuration);
        var reset = graph.ToProjectJson();
        reset.ShouldNotContain("#FF0000");
        reset.ShouldContain("#00FF00");
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(reset);
    }

    [Theory]
    [InlineData("main.missing", "#0000FF")]
    [InlineData("other.sink", "#0000FF")]
    [InlineData("main.source", "blue")]
    [InlineData("main.source", "url(example)")]
    public async Task Invalid_color_changes_leave_the_project_untouched(string address, string color)
    {
        var graph = Create();
        var before = graph.ToProjectJson();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var sending = chat.SendAsync("Change color", null);
        (await generator.Next()).Complete(ColorChange(address, color));
        await sending;
        chat.Preview.Proposal.ShouldBeNull();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    private static ProposalResult ColorChange(string address = "main.source", string? color = "#0000FF", bool continuing = false) => new()
    {
        Tool = new("set_component_color", System.Text.Json.JsonSerializer.SerializeToElement(new { Address = address, Color = color })),
        Message = "Change component color.", Continue = continuing
    };

    [Fact]
    public async Task Multiple_attachments_are_copied_at_send_and_do_not_follow_the_callers_list()
    {
        var graph = Create();
        graph.ApplyEdits(graph.PrepareEdits(Batch()));
        var before = graph.ToProjectJson();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var selected = new List<string> { "main.source", "main.map", "main.source" };
        var workflows = new List<string> { "other", "main", "other" };
        var files = new List<TextAttachment> { new("sample.json", "{\"Reading\":1,\"reading\":2}"), new("notes.txt", "Keep casing.\n") };
        var captured = files.ToArray();
        var images = new List<ImageAttachment> { ImageSamples.Png, ImageSamples.Jpeg };
        var capturedImages = images.ToArray();
        var sending = chat.SendAsync("Explain these", null, selectedComponents: selected, selectedWorkflows: workflows, attachments: files, images: images);
        var request = await generator.Next();
        selected.Clear();
        workflows.Clear();
        files.Clear();
        images.Clear();
        request.Request.Images.ShouldBe(capturedImages);
        chat.Turns.Single().Images.ShouldBe(capturedImages);
        request.Request.Images[0] = ImageSamples.Jpeg;
        chat.Turns.Single().Images.ShouldBe(capturedImages);
        request.Request.Attachments.ShouldBe(captured);
        chat.Turns.Single().Attachments.ShouldBe(captured);
        request.Request.Workflow.ShouldBe("main");
        request.Request.SelectedWorkflows.ShouldBe(new[] { "other", "main" });
        chat.Turns.Single().SelectedWorkflows.ShouldBe(new[] { "other", "main" });
        request.Request.SelectedComponents.ShouldBe(new[] { "main.source", "main.map" });
        chat.Turns.Single().SelectedComponents.ShouldBe(new[] { "main.source", "main.map" });
        request.Complete(new() { Message = "Two components." });
        await sending;
        graph.ToProjectJson().ShouldBe(before);
    }

    [Fact]
    public async Task Unknown_editing_mode_is_rejected_before_generation_or_changes()
    {
        var graph = Create();
        var before = graph.ToProjectJson();
        var generator = new FixedProposalGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => chat.SendAsync("Add a component.", null, (ChatEditingMode)99));
        chat.Turns.ShouldBeEmpty();
        chat.IsBusy.ShouldBeFalse();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    [Theory]
    [InlineData(ChatEditingMode.Ask)]
    [InlineData(ChatEditingMode.Automatic)]
    public async Task Steps_use_fresh_configuration_keep_the_original_workflow_and_undo_individually(ChatEditingMode mode)
    {
        var graph = Create();
        var original = graph.ToProjectJson();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        TextAttachment[] files = [new("sample.json", "{\"Reading\":12}")];
        var sending = chat.SendAsync("Add and configure a mapper.", null, mode, ["main.source"], ["other", "main"], files, [ImageSamples.Png]);
        var first = await generator.Next();
        first.Request.Stepwise.ShouldBeTrue();
        first.Request.Attachments.ShouldBe(files);
        first.Request.Images.ShouldBe(new[] { ImageSamples.Png });
        first.Request.Workflow.ShouldBe("main");
        first.Request.SelectedWorkflows.ShouldBe(new[] { "other", "main" });
        first.Request.SelectedComponents.ShouldBe(new[] { "main.source" });
        first.Complete(Add("main.new-map", more: true));
        if (mode == ChatEditingMode.Ask)
        {
            await sending;
            graph.ToProjectJson().ShouldBe(original);
            chat.IsBusy.ShouldBeTrue();
            sending = chat.ApplyAsync();
        }
        var second = await generator.Next();
        var added = graph.ToProjectJson();
        added.ShouldNotBe(original);
        second.Request.Configuration.ShouldBe(graph.ToJson());
        second.Request.Attachments.ShouldBe(files);
        second.Request.Images.ShouldBe(new[] { ImageSamples.Png });
        second.Request.SelectedWorkflows.ShouldBe(new[] { "other", "main" });
        second.Request.SelectedComponents.ShouldBe(new[] { "main.source" });
        second.Request.Steps.Single().Outcome.ShouldBe("Applied.");
        graph.SelectWorkflow("other");
        second.Complete(new()
        {
            Edits = [new ConfigureComponentEdit(Address("main.new-map"), new Dictionary<string, System.Text.Json.JsonElement?> { ["Expression"] = Json("\"Reading * 2\"") })],
            Continue = true,
            Message = "Set the mapping expression."
        });
        if (mode == ChatEditingMode.Ask) { await sending; sending = chat.ApplyAsync(); }
        var last = await generator.Next();
        last.Request.Workflow.ShouldBe("main");
        last.Request.Attachments.ShouldBe(files);
        last.Request.Images.ShouldBe(new[] { ImageSamples.Png });
        last.Request.SelectedWorkflows.ShouldBe(new[] { "other", "main" });
        last.Request.SelectedComponents.ShouldBe(new[] { "main.source" });
        last.Request.Steps.Length.ShouldBe(2);
        last.Request.Configuration.ShouldContain("Reading * 2");
        last.Complete(new() { Message = "The mapper is ready." });
        await sending;
        chat.IsBusy.ShouldBeFalse();
        chat.Preview.Proposal.ShouldBeNull();
        graph.WorkflowName.ShouldBe("other");
        var configured = graph.ToProjectJson();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(added);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(original);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo(); graph.Redo();
        graph.ToProjectJson().ShouldBe(configured);
        graph.SelectWorkflow("other");
        var question = chat.SendAsync("What does this workflow do?", null);
        var next = await generator.Next();
        next.Request.Workflow.ShouldBe("other");
        next.Request.SelectedComponents.ShouldBeEmpty();
        next.Request.SelectedWorkflows.ShouldBeEmpty();
        next.Request.Attachments.ShouldBeEmpty();
        next.Request.Instructions.ShouldContain("Previous request (main)");
        next.Complete(new() { Message = "This workflow contains a batch component." });
        await question;
        graph.ToProjectJson().ShouldBe(configured);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automatic_apply_rechecks_cancellation_and_manual_changes_after_preparing(bool manualChange)
    {
        var graph = Create();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var interrupted = false;
        chat.Changed += () =>
        {
            if (interrupted || chat.Preview.Proposal is null) return;
            interrupted = true;
            if (manualChange) { graph.ApplyEdits(graph.PrepareEdits(Batch())); graph.Undo(); }
            else chat.Discard();
        };
        var before = graph.ToProjectJson();
        var sending = chat.SendAsync("Add a mapper.", null, ChatEditingMode.Automatic);
        (await generator.Next()).Complete(Add("main.proposed", more: true));
        await sending;
        interrupted.ShouldBeTrue();
        graph.ToProjectJson().ShouldBe(before);
        generator.Count.ShouldBe(1);
        if (manualChange) chat.Preview.IsStale.ShouldBeTrue();
        chat.Discard();
        chat.IsBusy.ShouldBeFalse();
        chat.Preview.Proposal.ShouldBeNull();
        graph.CanUndo.ShouldBeFalse();
        graph.CanRedo.ShouldBe(manualChange);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_or_manual_changes_stop_automatic_continuation_without_rolling_back_completed_edits(bool manualChange)
    {
        var graph = Create();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var sending = chat.SendAsync("Add two components.", null, ChatEditingMode.Automatic);
        (await generator.Next()).Complete(Add("main.first", more: true));
        var next = await generator.Next();
        if (manualChange)
        {
            graph.ApplyEdits(graph.PrepareEdits([new AddComponentEdit(Address("main.manual"), "mapper")]));
            graph.Undo();
        }
        else { chat.Discard(); next.Token.IsCancellationRequested.ShouldBeTrue(); }
        var before = graph.ToProjectJson();
        next.Complete(Add("main.second", more: true));
        await sending;
        graph.ToProjectJson().ShouldBe(before);
        graph.ToJson().ShouldContain("first");
        graph.ToJson().ShouldNotContain("second");
        chat.IsBusy.ShouldBeFalse();
        chat.Preview.Proposal.ShouldBeNull();
        chat.Turns.Single().Response.ShouldContain(manualChange ? "design changed" : "cancelled");
        graph.CanUndo.ShouldBeTrue();
        if (manualChange) graph.CanRedo.ShouldBeTrue();
    }

    [Fact]
    public async Task Rejecting_a_step_does_not_continue_or_change_configuration()
    {
        var graph = Create();
        var before = graph.ToProjectJson();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var sending = chat.SendAsync("Add a mapper.", null);
        (await generator.Next()).Complete(Add("main.proposed", more: true));
        await sending;
        chat.Discard();
        chat.Turns.Single().Steps.Single().Outcome.ShouldBe("Discarded.");
        chat.IsBusy.ShouldBeFalse();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        generator.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Question_answer_does_not_change_configuration_or_redo()
    {
        var graph = Create();
        graph.ApplyEdits(graph.PrepareEdits(Batch())); graph.Undo();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var sending = chat.SendAsync("Explain the current mapper.", null, ChatEditingMode.Automatic);
        (await generator.Next()).Complete(new() { Message = "It returns the current value." });
        await sending;
        chat.Turns.Single().Steps.ShouldBeEmpty();
        chat.Turns.Single().Response.ShouldBe("It returns the current value.");
        graph.ToProjectJson().ShouldBe(before);
        graph.Revision.ShouldBe(revision);
        graph.CanRedo.ShouldBeTrue();
    }

    [Theory]
    [InlineData("main.missing")]
    [InlineData("other.sink")]
    public async Task Missing_or_other_workflow_attachment_is_rejected_before_generation(string component)
    {
        var graph = Create();
        var before = graph.ToProjectJson();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        await Should.ThrowAsync<ArgumentException>(() => chat.SendAsync("Explain these", null, selectedComponents: ["main.source", component]));
        generator.Count.ShouldBe(0);
        chat.Turns.ShouldBeEmpty();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_workflow_context_is_rejected_without_generation_or_edits()
    {
        var graph = Create();
        var before = graph.ToProjectJson();
        var generator = new ControlledGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        await Should.ThrowAsync<ArgumentException>(() => chat.SendAsync("Explain", null, selectedWorkflows: ["main", "missing"]));
        generator.Count.ShouldBe(0);
        chat.Turns.ShouldBeEmpty();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    private static DesignerGraphState Create()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Original, "Application");
        graph.SelectWorkflow("other"); graph.SelectWorkflow("main");
        graph.ResetHistory();
        return graph;
    }

    private static ProposalResult Add(string name, bool more) => new()
    {
        Edits = [new AddComponentEdit(Address(name), "mapper")], Continue = more, Message = "Add a mapper."
    };

    private sealed class ControlledGenerator : IProposalGenerator
    {
        private readonly Channel<Call> _calls = Channel.CreateUnbounded<Call>();
        public int Count { get; private set; }
        public Task<ProposalResult> GenerateAsync(ProposalRequest request, CancellationToken cancellationToken = default)
        {
            Count++;
            var call = new Call(request, cancellationToken);
            _calls.Writer.TryWrite(call);
            return call.Completion.Task;
        }
        public async Task<Call> Next() => await _calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed record Call(ProposalRequest Request, CancellationToken Token)
    {
        public TaskCompletionSource<ProposalResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(ProposalResult result) => Completion.SetResult(result);
    }
}
