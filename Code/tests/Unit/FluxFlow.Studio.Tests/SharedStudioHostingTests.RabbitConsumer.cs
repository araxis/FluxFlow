using System.Text.Json;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Plugins.ObjectGroups;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Extensions;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Fact]
    public async Task Rabbit_consumer_property_grid_preserves_saved_values_declarations_and_history()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""
            {"Resources":{"broker":{"Type":"amqp.connection"},"mqtt":{"Type":"mqtt.client"}},"Workflows":{"main":{
              "receive":{"Type":"amqp.consumer","cOnNeCtIoN":"missing","qUeUe":"orders","pReFeTcH":17,"Capacity":9,"MaxMessageBytes":4096,
                "Output":"log.Input","Future":{"Keep":true},"Topology":{"Queues":[{"Name":"orders","Arguments":{"x-message-ttl":{"Type":"Int32","Value":1000}}}]}},
              "log":{"Type":"logger"}}}}
            """);
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "receive");
        graph.Diagram.SelectModel(node, true);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var topology = node.Configuration["Topology"].GetRawText();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ComponentInspector>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentInspector.Model)] = node.Description.GetInspector(node.Configuration),
                [nameof(ComponentInspector.PaletteItem)] = node.Description.Palette,
                [nameof(ComponentInspector.Configuration)] = node.Configuration,
                [nameof(ComponentInspector.NodeKey)] = "main.receive",
                [nameof(ComponentInspector.NodeName)] = "receive",
                [nameof(ComponentInspector.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var html = view.ToHtmlString();
            html.ShouldContain("Advanced JSON");
            html.ShouldNotContain("Configure RabbitMQ");
            components.Items.OfType<ObjectGroupsGrid>().ShouldHaveSingleItem();
            components.Items.OfType<OptionEditorField>().Select(field => field.Option.Name).ShouldBe(new[] { "Connection", "Queue", "Prefetch", "Capacity", "MaxMessageBytes", "RequeueOnNack" });
            var connection = components.Items.OfType<MudSelect<string>>().Single();
            connection.GetState(item => item.Value).ShouldBe("missing");
            connection.GetState(item => item.Error).ShouldBeTrue();
            components.Items.OfType<MudSelectItem<string>>().Select(item => item.Value).ShouldBe(new[] { "Resources.broker", "missing" });
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            var prefetch = components.Items.OfType<ValidatedTextField>().Single(field => field.Label == "Prefetch");
            prefetch.Value.ShouldBe("17");
            await prefetch.ValueChanged.InvokeAsync("23");
            node.Configuration["pReFeTcH"].GetInt32().ShouldBe(23);
            node.Configuration.ContainsKey("Prefetch").ShouldBeFalse();
            node.Configuration["qUeUe"].GetString().ShouldBe("orders");
            node.Configuration["Topology"].GetRawText().ShouldBe(topology);
            node.Configuration["Future"].GetProperty("Keep").GetBoolean().ShouldBeTrue();
            node.Configuration["Output"].GetString().ShouldBe("log.Input");
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }
}
