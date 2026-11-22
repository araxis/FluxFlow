using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Authoring.Definitions.JetStream;
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
    [Theory]
    [InlineData("amqp", null)]
    [InlineData("amqp", "receive")]
    [InlineData("amqp", "removed")]
    [InlineData("amqp", "publish")]
    [InlineData("amqp", "elsewhere")]
    [InlineData("nats.jetstream", null)]
    [InlineData("nats.jetstream", "receive")]
    [InlineData("nats.jetstream", "removed")]
    [InlineData("nats.jetstream", "publish")]
    [InlineData("nats.jetstream", "elsewhere")]
    public async Task Component_selector_filters_current_workflow_and_preserves_missing_references_and_history(string family, string? saved)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"settle":{"Type":"FAMILY.settlement","Extra":42},"receive":{"Type":"FAMILY.consumer"},"other":{"Type":"FAMILY.consumer"},"publish":{"Type":"FAMILY.publisher"}},"remote":{"elsewhere":{"Type":"FAMILY.consumer"}}}}""".Replace("FAMILY", family));
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "settle");
        if (saved is not null) node.Configuration["cOnSuMeR"] = System.Text.Json.JsonSerializer.SerializeToElement(saved);
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "settle"), true);
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var options = family == "amqp"
                ? graph.FindDescription("amqp.settlement")!.Inspector.Sections.SelectMany(section => section.Options)
                : JetStreamDescription.Options("nats.jetstream.settlement");
            var option = options.Single(item => item.Name == "Consumer");
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var select = components.Items.OfType<MudSelect<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Consumer"));
            var items = components.Items.OfType<MudSelectItem<string>>().ToArray();
            items.Where(item => !item.Disabled).Select(item => item.Value).ShouldBe(["other", "receive"], ignoreOrder: true);
            select.Error.ShouldBe(saved is not (null or "receive"));
            if (select.Error) items.Single(item => item.Value == saved).Disabled.ShouldBeTrue();
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            foreach (var invalid in new[] { "", "publish", "elsewhere", "removed" })
            {
                await select.ValueChanged.InvokeAsync(invalid);
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            }
            if (saved == "receive")
            {
                await select.ValueChanged.InvokeAsync(saved);
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            }
            await select.ValueChanged.InvokeAsync("other");
            graph.SelectedNode!.Configuration[saved is null ? "Consumer" : "cOnSuMeR"].GetString().ShouldBe("other");
            graph.SelectedNode.Configuration["Extra"].GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
            graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "receive"), true);
            graph.DeleteSelected();
            var removed = graph.ToProjectJson();
            await select.ValueChanged.InvokeAsync("receive");
            graph.ToProjectJson().ShouldBe(removed);
        });
    }
}
