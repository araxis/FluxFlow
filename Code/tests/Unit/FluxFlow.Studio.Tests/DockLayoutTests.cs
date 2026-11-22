using FluxFlow.Studio.UI.Features.Designer.Components.Workspace;
using FluxFlow.Authoring.Evaluation;
using Shouldly;
using Xunit;

public class DockLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Previewing_an_auto_hidden_panel_preserves_layout_until_dropped(bool tabbed)
    {
        foreach (var edge in Enum.GetValues<DockPosition>().Cast<DockPosition?>().Append(null))
        {
            var layout = new DockLayout();
            if (tabbed) layout.Group("Input", "Message");
            layout.AutoHide("Input");
            var before = layout.Visit().Select(item => (item.Area, Tabs: string.Join(",", item.Node.Panels), item.Node.ActivePanel)).ToArray();
            var hidden = layout.AutoHiddenPanels.ToArray();
            var preview = edge is { } position ? layout.Preview("Input", position, "Expression")
                : layout.PreviewGroup("Input", "Expression");

            layout.Visit().Select(item => (item.Area, Tabs: string.Join(",", item.Node.Panels), item.Node.ActivePanel)).ShouldBe(before);
            layout.AutoHiddenPanels.ShouldBe(hidden);
            layout.Contains("Input").ShouldBeFalse();

            if (edge is { } destination) layout.Dock("Input", destination, "Expression");
            else layout.Group("Input", "Expression");
            layout.IsAutoHidden("Input").ShouldBeFalse();
            layout.Visit().Single(item => item.Node.Panels.Contains("Input")).Area.ShouldBe(preview);
            layout.Visit().SelectMany(item => item.Node.Panels).Order().ShouldBe(new[] { "Expression", "Input", "Message", "Result" });
        }
    }

    [Fact]
    public void Pin_restores_split_position_and_size_after_auto_hide()
    {
        var layout = new DockLayout(() => new()
        {
            Percent = 70,
            First = new() { Horizontal = true, Percent = 22, First = new("Components"),
                Second = new() { Horizontal = true, Percent = 62, First = new("Canvas"), Second = new("Inspector") } },
            Second = new("Observations")
        });
        var original = layout.Visit().Select(item => (item.Node.Panel, item.Area)).ToArray();
        foreach (var (panel, edge) in new[] { ("Components", DockPosition.Left), ("Inspector", DockPosition.Right), ("Observations", DockPosition.Bottom) })
        {
            layout.AutoHide(panel);
            layout.Contains(panel).ShouldBeFalse();
            layout.AutoHiddenPanels.ShouldContain((panel, edge));
            var peek = layout.PeekArea(panel);
            (peek.Width > 0 && peek.Height > 0 && peek.X + peek.Width <= 100 && peek.Y + peek.Height <= 100).ShouldBeTrue();
            layout.Pin(panel);
            layout.AutoHiddenPanels.ShouldBeEmpty();
            layout.Visit().Select(item => (item.Node.Panel, item.Area)).ShouldBe(original);
        }
    }

    [Fact]
    public void Auto_hidden_tabs_restore_order_and_close_removes_the_edge_tab()
    {
        var layout = new DockLayout();
        layout.Group("Message", "Input");
        layout.AutoHide("Input");
        layout.AutoHide("Input"); // Repeated requests are harmless.
        layout.Pin("Input");
        var group = layout.Visit().Single(item => item.Node.Panels.Contains("Input")).Node;
        group.Panels.ShouldBe(["Input", "Message"]);
        group.ActivePanel.ShouldBe("Input");
        layout.AutoHide("Input");
        layout.Hide("Input");
        layout.AutoHiddenPanels.ShouldBeEmpty();
        layout.Contains("Input").ShouldBeFalse();
        layout.Show("Input", DockPosition.Left);
        layout.Contains("Input").ShouldBeTrue();
    }

    [Fact]
    public void Auto_hide_keeps_one_docked_panel_and_recovers_when_neighbors_are_closed()
    {
        var layout = new DockLayout();
        layout.AutoHide("Input");
        layout.Hide("Message");
        layout.AutoHide("Result");
        layout.AutoHide("Expression");
        layout.Root.Panel.ShouldBe("Expression");
        layout.IsAutoHidden("Expression").ShouldBeFalse();
        layout.Show("Input", DockPosition.Right);
        layout.IsAutoHidden("Input").ShouldBeFalse();
        layout.Group("Result", "Input");
        layout.IsAutoHidden("Result").ShouldBeFalse();
        layout.Visit().Where(item => item.Node.Panel is not null).Sum(item => item.Area.Width * item.Area.Height).ShouldBe(10000, .001);
        layout.AutoHide("Result");
        layout.Reset();
        layout.AutoHiddenPanels.ShouldBeEmpty();
        layout.Visit().SelectMany(item => item.Node.Panels).Order().ShouldBe(new[] { "Expression", "Input", "Message", "Result" });
    }

    [Fact]
    public void Custom_workspace_can_hide_restore_group_and_reset_panels()
    {
        var layout = new DockLayout(() => new()
        {
            Horizontal = true, Percent = 22, First = new("Components"),
            Second = new() { First = new("Canvas"), Second = new("Inspector") }
        });
        var original = layout.Visit().Select(item => (item.Node.Panel, item.Area)).ToArray();
        layout.Group("Inspector", "Canvas");
        layout.Hide("Inspector");
        layout.Contains("Inspector").ShouldBeFalse();
        layout.Show("Inspector", DockPosition.Bottom);
        layout.Visit().Single(item => item.Node.Panel == "Inspector").Area.Width.ShouldBe(100);
        layout.Group("Inspector", "Canvas");
        layout.Show("Canvas", DockPosition.Left);
        layout.Visit().Single(item => item.Node.Panels.Contains("Canvas")).Node.ActivePanel.ShouldBe("Canvas");

        layout.Hide("Inspector");
        layout.Hide("Components");
        layout.Hide("Canvas"); // Keep one usable panel.
        layout.Root.Panel.ShouldBe("Canvas");
        layout.Dock("Canvas", DockPosition.Top);
        layout.Preview("Canvas", DockPosition.Left).ShouldBe(new DockLayout.Area(0, 0, 100, 100));
        layout.Reset();
        layout.Visit().Select(item => (item.Node.Panel, item.Area)).ShouldBe(original);
    }

    [Fact]
    public void Every_panel_can_dock_at_every_workspace_edge_and_beside_each_other()
    {
        var layout = new DockLayout();
        string[] panels = ["Input", "Expression", "Message", "Result"];
        foreach (var panel in panels)
        foreach (var edge in Enum.GetValues<DockPosition>())
        foreach (var target in panels.Where(name => name != panel).Prepend(null))
        {
            var before = layout.Visit().ToArray();
            var preview = layout.Preview(panel, edge, target);
            layout.Visit().ShouldBe(before);
            layout.Dock(panel, edge, target);
            var leaves = layout.Visit().Where(item => item.Node.Panel is not null).ToArray();
            leaves.Select(item => item.Node.Panel).Order().ShouldBe(panels.Order());
            leaves.Sum(item => item.Area.Width * item.Area.Height).ShouldBe(10000, .001);
            var moved = leaves.Single(item => item.Node.Panel == panel).Area;
            moved.ShouldBe(preview);
            if (target is null)
            {
                if (edge == DockPosition.Left) moved.X.ShouldBe(0);
                if (edge == DockPosition.Top) moved.Y.ShouldBe(0);
                if (edge == DockPosition.Right) (moved.X + moved.Width).ShouldBe(100, .001);
                if (edge == DockPosition.Bottom) (moved.Y + moved.Height).ShouldBe(100, .001);
            }
            else
            {
                var neighbor = leaves.Single(item => item.Node.Panel == target).Area;
                if (edge == DockPosition.Left) (moved.X + moved.Width).ShouldBe(neighbor.X, .001);
                if (edge == DockPosition.Right) (neighbor.X + neighbor.Width).ShouldBe(moved.X, .001);
                if (edge == DockPosition.Top) (moved.Y + moved.Height).ShouldBe(neighbor.Y, .001);
                if (edge == DockPosition.Bottom) (neighbor.Y + neighbor.Height).ShouldBe(moved.Y, .001);
            }
        }
        layout.Reset();
        layout.Visit().Single(item => item.Node.Panel == "Input").Area.X.ShouldBe(0);
        layout.Visit().Single(item => item.Node.Panel == "Result").Area.Width.ShouldBe(100);
    }

    [Fact]
    public void Group_preview_preserves_layout_and_tabs_can_split_back_out()
    {
        var layout = new DockLayout();
        var before = layout.Visit().Select(item => (item.Area, Tabs: string.Join(",", item.Node.Panels), item.Node.ActivePanel)).ToArray();
        var preview = layout.PreviewGroup("Result", "Input");
        layout.Visit().Select(item => (item.Area, Tabs: string.Join(",", item.Node.Panels), item.Node.ActivePanel)).ShouldBe(before);

        layout.Group("Result", "Input");
        var group = layout.Visit().Single(item => item.Node.Panels.Contains("Result"));
        group.Area.ShouldBe(preview);
        group.Node.Panels.ShouldBe(["Input", "Result"]);
        group.Node.ActivePanel.ShouldBe("Result");
        layout.Group("Expression", "Result");
        layout.Group("Message", "Input");
        layout.Visit().Count().ShouldBe(1);
        layout.Root.Panels.Count.ShouldBe(4);
        layout.Group("Input", "Result");
        layout.Root.Panels.Count.ShouldBe(4);

        foreach (var panel in new[] { "Message", "Expression", "Result" })
        {
            var splitPreview = layout.Preview(panel, DockPosition.Right, "Input");
            layout.Dock(panel, DockPosition.Right, "Input");
            layout.Visit().Single(item => item.Node.Panels.Contains(panel)).Area.ShouldBe(splitPreview);
            var leaves = layout.Visit().Where(item => item.Node.Panel is not null).ToArray();
            leaves.SelectMany(item => item.Node.Panels).Order().ShouldBe(new[] { "Expression", "Input", "Message", "Result" });
            leaves.Sum(item => item.Area.Width * item.Area.Height).ShouldBe(10000, .001);
            foreach (var leaf in leaves) leaf.Node.Panels.ShouldContain(leaf.Node.ActivePanel!);
        }
        layout.Reset();
        layout.Visit().Count(item => item.Node.Panels.Count == 1).ShouldBe(4);
    }

    [Fact]
    public void Context_display_keeps_timestamp_offset_readable()
    {
        var context = new ExpressionEvaluationRequest().Context;
        context.ShouldContain("+00:00");
        context.ShouldNotContain("\\u002B");
    }
}
