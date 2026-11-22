using System.Text.Json;
using FluxFlow.Plugins;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    public static IEnumerable<object[]> ComponentCatalog() =>
        StudioTestServices.InstalledCatalog.Append(new FluxFlow.Plugins.Serial.SerialPlugin().Description)
            .SelectMany(plugin => plugin.Components.Select(component => new object[] { component.Palette.ComponentType }));

    [Theory]
    [MemberData(nameof(ComponentCatalog))]
    public async Task Every_component_inspector_renders_without_mutating_saved_data_and_survives_reopen(string type)
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var description = graph.FindDescription(type)!;
        description.ShouldNotBeNull();
        var node = graph.AddNode(description);
        node.Configuration["FutureSetting"] = JsonSerializer.SerializeToElement(new { Value = "preserve me" });
        graph.RecordEdit();
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        graph.Diagram.SelectModel(node, true);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ComponentInspector>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentInspector.Model)] = description.GetInspector(node.Configuration),
                [nameof(ComponentInspector.PaletteItem)] = description.Palette,
                [nameof(ComponentInspector.Configuration)] = node.Configuration,
                [nameof(ComponentInspector.NodeKey)] = $"{graph.WorkflowName}.{node.NodeName}",
                [nameof(ComponentInspector.NodeName)] = node.NodeName,
                [nameof(ComponentInspector.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            view.ToHtmlString().ShouldContain("property-row");
            if (description.GetInspector(node.Configuration).Sections.SelectMany(section => section.Options)
                .Any(option => option.Editor == FluxFlow.Authoring.OptionEditorKind.Expression))
                view.ToHtmlString().ShouldContain("colspan=\"2\" class=\"expression-property\"");
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
        });
        graph.Diagram.SelectModel(node, true);
        graph.DeleteSelected();
        graph.RecordEdit();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.Redo();
        graph.Diagram.Nodes.ShouldBeEmpty();
        var missing = new DesignerGraphState([]);
        missing.LoadProject(before);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(missing.ToProjectJson());
        reopened.ToProjectJson().ShouldBe(before);
    }
}
