using FluxFlow.Authoring;
using FluxFlow.Studio.UI.Features.Connections;
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
    [Fact]
    public async Task Effective_connection_view_refreshes_on_environment_switch_and_undo_without_exposing_passwords()
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(EffectiveConnectionSettingsTests.Configuration);
        var manual = graph.ActiveEnvironmentId;
        graph.AddEnvironment("Service environment");
        var bound = graph.ActiveEnvironmentId;
        graph.SetTopology(new() { Nodes = [
            new() { Id = "broker", Kind = FluxFlow.Environments.EnvironmentNodeKind.Service, Name = "Selected broker", ExistingService = new()
                { Endpoint = "mqtts://service:8883", Mqtt = new() { Username = "service-user", Password = "service-secret" } } },
            new() { Kind = FluxFlow.Environments.EnvironmentNodeKind.Application, Name = "Instance A", Bindings = new() { ["Resources:broker"] = "broker" } }
        ] });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(ConnectionInspector.Selection)] = "Resources:broker" }));
            view.ToHtmlString().ShouldContain("Bound service"); view.ToHtmlString().ShouldContain("service-user");
            view.ToHtmlString().ShouldNotContain("service-secret");
            // Manual secrets belong to masked editor inputs, never the read-only effective summary.
            view.ToHtmlString().Split("Effective settings")[1].ShouldNotContain("manual-secret");
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.SelectEnvironment(manual);
            await Task.Yield();
            view.ToHtmlString().ShouldContain("manual-user"); view.ToHtmlString().ShouldNotContain("service-user");
            graph.SelectEnvironment(bound);
            var topology = graph.GetEnvironmentDefinition().Topology;
            topology.Nodes.Single(node => node.Kind == FluxFlow.Environments.EnvironmentNodeKind.Application).Bindings.Clear();
            graph.SetTopology(topology);
            await Task.Yield();
            view.ToHtmlString().ShouldContain("manual-user");
            graph.Undo();
            await Task.Yield();
            view.ToHtmlString().ShouldContain("service-user");
            graph.ToProjectJson().ShouldBe(before); graph.ExportConfiguration().ShouldBe(configuration);
        });
    }

    [Theory]
    [InlineData("mqtt.client")]
    [InlineData("amqp.connection")]
    [InlineData("http.client")]
    [InlineData("nats.connection")]
    [InlineData("ble.adapter")]
    public async Task Related_connection_fields_use_filtered_dropdowns_and_preserve_missing_values_and_history(string resourceType)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"target":{"Type":"resource-type"},"other":{"Type":"incompatible"}},"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","cOnNeCtIoN":"removed","Custom":42}}}}""".Replace("resource-type", resourceType));
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = OptionEditorModel.Create("Connection", "Connection") with { RelatedResource = resourceType, IsRequired = true },
                [nameof(OptionEditorField.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var select = components.Items.OfType<MudSelect<string>>().Single();
            select.Value.ShouldBe("removed");
            select.Error.ShouldBeTrue();
            components.Items.OfType<MudTextField<string>>().ShouldBeEmpty();
            var items = components.Items.OfType<MudSelectItem<string>>().ToArray();
            items.Select(item => item.Value).ShouldBe(new[] { "Resources.target", "removed" });
            items.Single(item => item.Value == "removed").Disabled.ShouldBeTrue();
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            await select.ValueChanged.InvokeAsync("Resources.target");
            graph.SelectedNode.Configuration["cOnNeCtIoN"].GetString().ShouldBe("Resources.target");
            graph.SelectedNode.Configuration["Custom"].GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        });
    }
}
