using FluxFlow.Conversations;
using FluxFlow.Studio.UI.Features.Designer;
using Shouldly;
using Xunit;
using static FluxFlow.Studio.Tests.ConfigurationEditingTests;

namespace FluxFlow.Studio.Tests;

public sealed class StudioChatTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partial_text_does_not_edit_and_late_chunks_cannot_reenter_a_cancelled_chat(bool manualEdit)
    {
        var graph = Create();
        var before = graph.ToProjectJson();
        var generator = new DelayedGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var sending = chat.SendAsync("Add", null, ChatEditingMode.Automatic, ["main.source"], ["other", "main"]);
        generator.Request!.SelectedWorkflows.ShouldBe(new[] { "other", "main" });
        generator.Request!.SelectedComponents.ShouldBe(new[] { "main.source" });
        var oldText = generator.OnText!;
        await oldText("First ");
        await oldText("part");
        chat.Turns.Single().Response.ShouldBe("First part");
        chat.Preview.Proposal.ShouldBeNull();
        graph.ToProjectJson().ShouldBe(before);
        if (manualEdit)
        {
            graph.ApplyEdits(graph.PrepareEdits(Batch()));
            graph.Undo();
            generator.Complete();
            await sending;
            chat.Turns.Single().Response.ShouldContain("design changed");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanRedo.ShouldBeTrue();
        }
        else
        {
            var oldCompletion = generator.Completion;
            chat.Clear();
            var next = chat.SendAsync("Next", null);
            generator.Request!.SelectedComponents.ShouldBeEmpty();
            generator.Request.SelectedWorkflows.ShouldBeEmpty();
            await oldText("Late text");
            oldCompletion.SetResult(FixedProposalGenerator.Result());
            await sending;
            chat.Turns.Single().Response.ShouldBe("Responding…");
            generator.Complete();
            await next;
            graph.ToProjectJson().ShouldBe(before);
        }
    }

    private static DesignerGraphState Create()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Original, "Application");
        graph.ResetHistory();
        return graph;
    }

    [Fact]
    public async Task Chat_preview_apply_and_undo_preserve_the_original_project()
    {
        var graph = Create();
        var before = graph.ToProjectJson();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), new FixedProposalGenerator());
        await chat.SendAsync("Add a source and mapper.", null);
        graph.ToProjectJson().ShouldBe(before);
        chat.Turns.Single().Response.ShouldContain("ready to review");
        await chat.ApplyAsync();
        chat.Turns.Single().Response.ShouldContain("applied");
        var after = graph.ToProjectJson();
        after.ShouldNotBe(before);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public async Task Follow_up_uses_current_configuration_and_connection_without_automatically_applying()
    {
        var graph = Create();
        var generator = new DelayedGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var first = chat.SendAsync("Add a source.", new() { Model = "first" });
        generator.Complete();
        await first;
        await Should.ThrowAsync<InvalidOperationException>(() => chat.SendAsync("Overlap", null));
        chat.Discard();
        graph.ApplyEdits(graph.PrepareEdits([new ConfigureComponentEdit(
            Address("main.source"), new Dictionary<string, System.Text.Json.JsonElement?> { ["Custom"] = Json("{\"KeepCASE\":true}") })]));
        var before = graph.ToProjectJson();
        var next = chat.SendAsync("Add a mapper too.", new() { Model = "second" });
        generator.Request!.Instructions.ShouldContain("Add a source.");
        generator.Request.Instructions.ShouldContain("discarded");
        generator.Request.Instructions.ShouldContain("Add a mapper too.");
        generator.Request.Configuration.ShouldBe(graph.ToJson());
        generator.Request.Connection!.Model.ShouldBe("second");
        generator.Complete();
        await next;
        graph.ToProjectJson().ShouldBe(before);
        chat.Clear();
        chat.Turns.ShouldBeEmpty();
        chat.Preview.Proposal.ShouldBeNull();
        graph.ToProjectJson().ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_and_new_chat_ignore_late_responses(bool fail)
    {
        var graph = Create();
        var generator = new DelayedGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var first = chat.SendAsync("First", null);
        var old = generator.Completion;
        var token = generator.Token;
        chat.Clear();
        token.IsCancellationRequested.ShouldBeTrue();
        var second = chat.SendAsync("Second", null);
        if (fail) old.SetException(new HttpRequestException("Late failure"));
        else old.SetResult(FixedProposalGenerator.Result());
        await first;
        chat.Turns.Single().Request.ShouldBe("Second");
        chat.Turns.Single().Response.ShouldBe("Responding…");
        chat.Preview.Proposal.ShouldBeNull();
        chat.Preview.IsGenerating.ShouldBeTrue();
        generator.Complete();
        await second;
        chat.Turns.Single().Response.ShouldContain("ready to review");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_edits_reject_generation_or_pending_apply(bool duringGeneration)
    {
        var graph = Create();
        var generator = new DelayedGenerator();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var sending = chat.SendAsync("Add a source.", null);
        if (!duringGeneration) { generator.Complete(); await sending; }
        graph.ApplyEdits(graph.PrepareEdits(Batch()));
        graph.Undo();
        var before = graph.ToProjectJson();
        if (duringGeneration)
        {
            generator.Complete();
            await sending;
            chat.Preview.Proposal.ShouldBeNull();
            chat.Turns.Single().Response.ShouldContain("design changed");
        }
        else
        {
            chat.Preview.IsStale.ShouldBeTrue();
            await Should.ThrowAsync<InvalidOperationException>(chat.ApplyAsync);
        }
        graph.ToProjectJson().ShouldBe(before);
        graph.CanRedo.ShouldBeTrue();
    }

    [Fact]
    public async Task Disposing_chat_cancels_generation_and_never_changes_configuration()
    {
        var graph = Create();
        var before = graph.ToProjectJson();
        var generator = new DelayedGenerator();
        var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        var sending = chat.SendAsync("Add a source.", null);
        chat.Dispose();
        generator.Token.IsCancellationRequested.ShouldBeTrue();
        generator.Complete();
        await sending;
        chat.Preview.Proposal.ShouldBeNull();
        graph.ToProjectJson().ShouldBe(before);
    }

    private sealed class DelayedGenerator : IProposalGenerator
    {
        public Func<string, Task>? OnText { get; private set; }
        public Task<ProposalResult> GenerateAsync(ProposalRequest request, Func<string, Task> onText, CancellationToken cancellationToken = default)
        { OnText = onText; return GenerateAsync(request, cancellationToken); }
        public ProposalRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource<ProposalResult> Completion { get; private set; } = new();
        public Task<ProposalResult> GenerateAsync(ProposalRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            Token = cancellationToken;
            Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return Completion.Task;
        }
        public void Complete() => Completion.SetResult(FixedProposalGenerator.Result());
    }
}
