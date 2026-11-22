using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Shouldly;
using Xunit;
using static FluxFlow.Studio.Tests.ConfigurationEditingTests;

namespace FluxFlow.Studio.Tests;

public sealed class StudioProposalPreviewTests
{
    private static DesignerGraphState Create()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Original, "Application");
        graph.ResetHistory();
        return graph;
    }

    [Fact]
    public async Task Preview_and_discard_preserve_configuration_layout_and_redo()
    {
        var graph = Create();
        graph.ApplyEdits(graph.PrepareEdits(Batch()));
        graph.Undo();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        var preview = new ConversationPreview(new StudioConversationWorkspace(graph), new FixedProposalGenerator());
        await preview.GenerateAsync("Add a source and mapper.");
        preview.Before.ShouldBe(graph.ToJson());
        preview.Workflow.ShouldBe("main");
        preview.Proposal!.Diagnostics.ShouldNotContain(item => item.Severity == ValidationSeverity.Error);
        var candidate = new DesignerConfiguration(preview.Proposal.Configuration, "Application");
        var nodes = candidate.Nodes("main");
        nodes.Single(node => node.Address.ComponentName == "sample-source").Values["Values"].GetString()
            .ShouldBe("[{\"Reading\":7},{\"Reading\":12}]");
        var mapper = nodes.Single(node => node.Address.ComponentName == "sample-mapper");
        mapper.Values["Engine"].GetString().ShouldBe("jsonata");
        mapper.Values["Expression"].GetString().ShouldBe("{\"Reading\": Reading * 2}");
        preview.Discard();
        preview.Proposal.ShouldBeNull();
        preview.Before.ShouldBeNull();
        preview.Workflow.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => preview.Apply());
        graph.ToProjectJson().ShouldBe(before);
        graph.Revision.ShouldBe(revision);
        graph.CanRedo.ShouldBeTrue();
    }

    [Fact]
    public async Task Apply_preserves_existing_project_and_is_one_undo_action()
    {
        var graph = Create();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        node.SetPosition(25, 70);
        graph.Diagram.SelectModel(node, true);
        graph.SetSelectedNodeColor("#abcdef");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var preview = new ConversationPreview(new StudioConversationWorkspace(graph), new FixedProposalGenerator());
        await preview.GenerateAsync("Add a source and mapper.");
        preview.Apply().ShouldBeTrue();
        preview.Proposal.ShouldBeNull();
        graph.NodeCount.ShouldBe(3);
        graph.Diagram.Links.Count.ShouldBe(1);
        var after = graph.ToProjectJson();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(after);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_edits_invalidate_preview_even_after_undo(bool undo)
    {
        var graph = Create();
        var preview = new ConversationPreview(new StudioConversationWorkspace(graph), new FixedProposalGenerator());
        await preview.GenerateAsync("Add a source and mapper.");
        var proposal = preview.Proposal;
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration["expression"] = Json("\"manual\"");
        graph.RecordEdit();
        if (undo) graph.Undo();
        var before = graph.ToProjectJson();
        preview.IsStale.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => preview.Apply()).Message.ShouldContain("design changed");
        preview.Proposal.ShouldBeSameAs(proposal);
        graph.ToProjectJson().ShouldBe(before);
        preview.Discard();
        await preview.GenerateAsync("Add a source and mapper.");
        preview.IsStale.ShouldBeFalse();
        preview.Before.ShouldBe(graph.ToJson());
    }

    [Fact]
    public async Task Unfinished_manual_edit_keeps_pending_preview_until_it_can_be_applied()
    {
        var graph = Create();
        var preview = new ConversationPreview(new StudioConversationWorkspace(graph), new FixedProposalGenerator());
        await preview.GenerateAsync("Add a source and mapper.");
        var proposal = preview.Proposal;
        using (graph.BeginManualEdit())
            Should.Throw<InvalidOperationException>(() => preview.Apply()).Message.ShouldContain("Finish or cancel");
        preview.Proposal.ShouldBeSameAs(proposal);
        preview.Apply().ShouldBeTrue();
    }

    [Fact]
    public async Task Pending_preview_and_existing_sample_names_are_not_overwritten()
    {
        var graph = Create();
        var preview = new ConversationPreview(new StudioConversationWorkspace(graph), new FixedProposalGenerator());
        await preview.GenerateAsync("Add a source and mapper.");
        var proposal = preview.Proposal;
        await Should.ThrowAsync<InvalidOperationException>(() => preview.GenerateAsync("Add a source and mapper."));
        preview.Proposal.ShouldBeSameAs(proposal);
        preview.Apply();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        await Should.ThrowAsync<ArgumentException>(() => preview.GenerateAsync("Add a source and mapper."));
        preview.Proposal.ShouldBeNull();
        graph.ToProjectJson().ShouldBe(before);
        graph.Revision.ShouldBe(revision);
    }

    [Fact]
    public async Task Navigation_keeps_preview_target_and_current_workflow()
    {
        var graph = Create();
        var preview = new ConversationPreview(new StudioConversationWorkspace(graph), new FixedProposalGenerator());
        await preview.GenerateAsync("Add a source and mapper.");
        graph.SelectWorkflow("other");
        preview.Workflow.ShouldBe("main");
        preview.IsStale.ShouldBeFalse();
        preview.Apply();
        graph.WorkflowName.ShouldBe("other");
        var document = new DesignerConfiguration(graph.ToJson(), "Application");
        document.Nodes("main").Count.ShouldBe(3);
        document.Nodes("other").Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncheckpointed_edits_and_document_replacement_reject_application(bool replace)
    {
        var graph = Create();
        var preview = new ConversationPreview(new StudioConversationWorkspace(graph), new FixedProposalGenerator());
        await preview.GenerateAsync("Add a source and mapper.");
        var proposal = preview.Proposal;
        if (replace) graph.LoadJson("""{"Workflows":{"replacement":{}}}""");
        else graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration["expression"] = Json("\"manual\"");
        var before = graph.ToProjectJson();
        Should.Throw<InvalidOperationException>(() => preview.Apply()).Message.ShouldContain("design changed");
        preview.IsStale.ShouldBeTrue();
        preview.Proposal.ShouldBeSameAs(proposal);
        preview.Discard();
        graph.ToProjectJson().ShouldBe(before);
    }

    [Fact]
    public async Task Closed_project_rejects_preparation_without_changing_history()
    {
        var graph = Create();
        graph.IsProjectOpen = false;
        graph.LoadJson("""{"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        var preview = new ConversationPreview(new StudioConversationWorkspace(graph), new FixedProposalGenerator());
        (await Should.ThrowAsync<InvalidOperationException>(() => preview.GenerateAsync("Add a source and mapper."))).Message.ShouldContain("Open or create");
        preview.Proposal.ShouldBeNull();
        graph.ToProjectJson().ShouldBe(before);
        graph.Revision.ShouldBe(revision);
        graph.CanUndo.ShouldBeFalse();
        graph.IsProjectOpen = true;
        await preview.GenerateAsync("Add a source and mapper.");
        preview.Proposal.ShouldNotBeNull();
    }
}
