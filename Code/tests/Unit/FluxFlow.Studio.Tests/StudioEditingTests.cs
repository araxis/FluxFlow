using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Shouldly;
using Xunit;
using static FluxFlow.Studio.Tests.ConfigurationEditingTests;

namespace FluxFlow.Studio.Tests;

public sealed class StudioEditingTests
{
    private static DesignerGraphState Create()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Original, "Application");
        graph.ResetHistory();
        return graph;
    }

    [Fact]
    public void Batch_appears_on_canvas_as_one_reversible_change()
    {
        var graph = Create();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        node.SetPosition(-10, 500);
        graph.Diagram.SelectModel(node, true);
        graph.SetSelectedNodeColor("#abcdef");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var file = graph.ProjectFile;
        var proposal = graph.PrepareEdits(Batch());
        proposal.Diagnostics.ShouldNotContain(item => item.Severity == ValidationSeverity.Error);
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        var observations = new List<int>();
        graph.Changed += () => observations.Add(graph.Diagram.Nodes.Count);
        graph.ApplyEdits(proposal).ShouldBeTrue();
        observations.ShouldBe(new[] { 2 });
        graph.Diagram.Nodes.Count.ShouldBe(2);
        graph.Diagram.Links.Count.ShouldBe(1); // The cross-workflow link stays in configuration.
        var after = graph.ToProjectJson();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(after);
        graph.ProjectFile.ShouldBe(file);
    }

    [Theory]
    [InlineData("configure")]
    [InlineData("move")]
    [InlineData("undo")]
    [InlineData("replace")]
    public void Manual_changes_reject_a_pending_proposal_without_losing_the_manual_edit(string change)
    {
        var graph = Create();
        var proposal = graph.PrepareEdits(Batch());
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        switch (change)
        {
            case "configure": node.Configuration["expression"] = Json("\"manual\""); break;
            case "move": node.SetPosition(900, 300); break;
            case "undo": node.SetPosition(900, 300); graph.RecordEdit(); graph.Undo(); break;
            case "replace": graph.LoadJson(Original, "Application"); break;
        }
        var before = graph.ToProjectJson();
        Should.Throw<InvalidOperationException>(() => graph.ApplyEdits(proposal)).Message.ShouldContain("design changed");
        graph.ToProjectJson().ShouldBe(before);
        graph.Diagram.Nodes.Count.ShouldBe(1);
    }

    [Fact]
    public void Selection_zoom_and_workflow_navigation_do_not_invalidate_a_proposal()
    {
        var graph = Create();
        var proposal = graph.PrepareEdits(Batch());
        graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
        graph.Diagram.SetZoom(.5);
        graph.SelectWorkflow("other");
        graph.ApplyEdits(proposal).ShouldBeTrue();
        graph.WorkflowName.ShouldBe("other");
    }

    [Fact]
    public void Pending_manual_edit_blocks_application_and_cancellation_keeps_proposal_valid()
    {
        var graph = Create();
        var proposal = graph.PrepareEdits(Batch());
        using (graph.BeginManualEdit())
            Should.Throw<InvalidOperationException>(() => graph.ApplyEdits(proposal)).Message.ShouldContain("Finish or cancel");
        graph.ApplyEdits(proposal).ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => graph.ApplyEdits(proposal));
    }

    [Fact]
    public void Noop_preserves_redo_and_proposals_cannot_cross_sessions()
    {
        var graph = Create();
        graph.ApplyEdits(graph.PrepareEdits(Batch()));
        graph.Undo();
        var revision = graph.Revision;
        graph.ApplyEdits(graph.PrepareEdits([])).ShouldBeFalse();
        graph.Revision.ShouldBe(revision);
        graph.CanRedo.ShouldBeTrue();
        var other = Create();
        Should.Throw<InvalidOperationException>(() => other.ApplyEdits(graph.PrepareEdits(Batch())));
    }

    [Fact]
    public void Invalid_connection_does_not_mutate_canvas_or_history()
    {
        var graph = Create();
        var before = graph.ToProjectJson();
        Should.Throw<ArgumentException>(() => graph.PrepareEdits([
            .. Batch(), new ConnectPortsEdit(Address("main.source").Port("Input"), Address("other.sink").Port("Input"))]));
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void Failed_canvas_projection_restores_configuration_layout_and_history()
    {
        var description = new FailingDescription();
        var graph = new DesignerGraphState([description]);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        var proposal = graph.PrepareEdits([new AddComponentEdit(Address("main.broken"), "broken")]);
        description.Fail = true;
        Should.Throw<InvalidOperationException>(() => graph.ApplyEdits(proposal)).ShouldBeSameAs(description.Failure);
        graph.ToProjectJson().ShouldBe(before);
        graph.Revision.ShouldBe(revision);
        graph.CanUndo.ShouldBeFalse();
        graph.NodeCount.ShouldBe(0);
    }

    private sealed class FailingDescription : IComponentDescription
    {
        public InvalidOperationException Failure { get; } = new("Presentation failed.");
        public bool Fail { get; set; }
        public PaletteItemModel Palette { get; } = new() { ComponentType = "broken", DisplayName = "Broken", Category = "Test" };
        public NodeInspectorModel Inspector { get; } = new() { ComponentType = "broken" };
        public PaletteItemModel GetPresentation(IReadOnlyDictionary<string, System.Text.Json.JsonElement> configuration) =>
            Fail ? throw Failure : Palette;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_application_keeps_current_workflow_and_selection_after_navigation(bool selectLink)
    {
        var description = new FailingDescription();
        var graph = new DesignerGraphState([.. Create().Components, description]);
        var document = new DesignerConfiguration(Original, "Application").PrepareEdits([
            new ConnectPortsEdit(Address("other.sink").Port("Output"), Address("other.sink").Port("Input"))]);
        graph.LoadJson(document.ToJson(), "Application");
        graph.ResetHistory();
        graph.SelectWorkflow("other");
        var sink = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        if (selectLink) graph.Diagram.SelectModel(graph.Diagram.Links.Single(), true);
        else graph.Diagram.SelectModel(sink, true);
        var before = graph.ToProjectJson();
        var proposal = graph.PrepareEdits([new AddComponentEdit(Address("other.broken"), "broken")]);
        description.Fail = true;
        Should.Throw<InvalidOperationException>(() => graph.ApplyEdits(proposal));
        graph.WorkflowName.ShouldBe("other");
        if (selectLink) graph.SelectedLink.ShouldNotBeNull();
        else graph.SelectedNode?.NodeName.ShouldBe("sink");
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }
}
