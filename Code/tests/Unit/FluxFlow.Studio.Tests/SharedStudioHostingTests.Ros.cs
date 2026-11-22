using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Ros2;
using FluxFlow.Studio.UI;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Plugins;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Rcl.Qos;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Fact]
    public void ROS_plugin_preserves_saved_wiring_and_history_when_missing_and_reinstalled()
    {
        var plugin = new FluxFlow.Plugins.Ros2.RosPlugin();
        foreach (var component in plugin.Description.Components)
        {
            component.Palette.Summary!.ShouldContain("std_msgs/msg/String");
            component.Editor.ShouldBe("properties");
        }
        typeof(FluxFlow.Plugins.Ros2.RosDesign).Assembly.GetReferencedAssemblies()
            .ShouldNotContain(assembly => assembly.Name == "Rcl.NET");
        var original = StudioTestServices.CreateGraph();
        original.LoadJson("""
            {"Resources":{"ros":{"Type":"ros.node","Name":"local","Future":42}},"Workflows":{"main":{
              "receive":{"Type":"ros.subscriber","Node":"ros","Topic":"/in","FullMode":"DropWrite","QueueSize":3,
                "qOs":{"hIsToRy":1,"Depth":7},"Output":"send.Input"},
              "send":{"Type":"ros.publisher","Node":"ros","Topic":"/out","OutputCapacity":9}}}}
            """);
        var saved = original.ToProjectJson();
        var canonical = JsonNode.Parse(saved);
        foreach (var installed in new[] { true, false, true })
        {
            using var services = new ServiceCollection().AddStudioUI(installed ? [plugin.Description] : []).BuildServiceProvider();
            var graph = new DesignerGraphState(services.GetServices<FluxFlow.Authoring.IComponentDescription>());
            graph.LoadProject(saved);
            graph.ResetHistory();
            saved = graph.ToProjectJson();
            JsonNode.DeepEquals(JsonNode.Parse(saved), canonical).ShouldBeTrue();
            graph.SetPortColorOverrides(new(Output: "#123456"));
            var edited = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(saved);
            graph.Redo(); graph.ToProjectJson().ShouldBe(edited);
        }
    }

    [Fact]
    public async Task ROS_node_uses_the_docked_grid_and_preserves_its_identity_and_unknown_settings()
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var description = scope.ServiceProvider.GetServices<ConnectionDescription>().Single(item => item.Type == "ros.node");
        var settings = description.Create(); description.Validate!(settings);
        settings["Namespace"] = " "; Should.Throw<ArgumentException>(() => description.Validate(settings));
        graph.LoadJson("""{"Resources":{"sensor":{"Type":"ros.node","nAmE":"local","Future":{"Id":"001"}}},"Workflows":{"main":{"send":{"Type":"ros.publisher","Node":"sensor","Topic":"/out"}}}}""");
        graph.ResetHistory(); var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(ConnectionInspector.Selection)] = "Resources:sensor" }));
            view.ToHtmlString().ShouldNotContain("Effective settings");
            var name = components.Items.OfType<MudTextField<string>>().Single(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Node name"));
            await name.ValueChanged.InvokeAsync(" "); graph.ToProjectJson().ShouldBe(before);
            await name.ValueChanged.InvokeAsync("remote");
        });
        var node = graph.Connections.Single(); node.Name.ShouldBe("sensor"); node.Settings["nAmE"]!.GetValue<string>().ShouldBe("remote");
        node.Settings["Future"]!["Id"]!.GetValue<string>().ShouldBe("001"); node.Settings.ContainsKey("Namespace").ShouldBeFalse();
        var after = graph.ToProjectJson(); graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after); graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }
    [Theory]
    [InlineData("KeepLast", true)]
    [InlineData("KeepAll", false)]
    [InlineData("2", false)]
    [InlineData("Default", true)]
    [InlineData("future", true)]
    public async Task ROS_QoS_history_hides_only_irrelevant_depth_without_deleting_it(string history, bool depth)
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var configuration = new Dictionary<string, JsonElement>
        {
            ["Type"] = JsonSerializer.SerializeToElement("ros.publisher"),
            ["Qos"] = JsonSerializer.SerializeToElement(new { hIsToRy = history, Depth = 19 })
        };
        var before = JsonSerializer.Serialize(configuration);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(ComponentConfiguratorComponent.Configuration)] = configuration }));
            view.ToHtmlString().Contains("aria-label=\"Depth\"").ShouldBe(depth);
            JsonSerializer.Serialize(configuration).ShouldBe(before);
        });
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("\"Wait\"", true)]
    [InlineData("0", true)]
    [InlineData("\"DropOldest\"", false)]
    public void ROS_subscriber_keeps_unsupported_saved_queue_modes_but_does_not_offer_them_for_new_settings(string? saved, bool unsupported)
    {
        var configuration = new Dictionary<string, JsonElement>();
        if (saved is not null) configuration["fUlLmOdE"] = JsonSerializer.Deserialize<JsonElement>(saved);
        var before = JsonSerializer.Serialize(configuration);
        var description = new FluxFlow.Authoring.Definitions.RosSubscriber.RosSubscriberDescription();
        var choices = description.GetInspector(configuration).Sections.SelectMany(section => section.Options).Single(option => option.Name == "FullMode").Choices;
        choices.Any(choice => choice.DisplayName == "Wait (unsupported)").ShouldBe(unsupported);
        choices.Any(choice => choice.DisplayName == "Wait").ShouldBeFalse();
        if (saved is not null) choices.ShouldContain(choice => choice.Value == configuration["fUlLmOdE"].ToString());
        JsonSerializer.Serialize(configuration).ShouldBe(before);
    }

    [Theory]
    [InlineData("ros.publisher", null)]
    [InlineData("ros.subscriber", null)]
    [InlineData("ros.publisher", "null")]
    [InlineData("ros.subscriber", "{}")]
    [InlineData("ros.publisher", "{}")]
    [InlineData("ros.subscriber", "{\"Reliability\":1}")]
    [InlineData("ros.publisher", "{\"dEpTh\":17,\"Reliability\":1,\"Extra\":{\"Keep\":42}}")]
    public async Task ROS_QoS_visual_edits_preserve_native_defaults_unknown_values_and_history(string type, string? qos)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var configuration = new Dictionary<string, JsonElement>
        {
            ["Type"] = JsonSerializer.SerializeToElement(type), ["Node"] = JsonSerializer.SerializeToElement("missing"),
            ["Topic"] = JsonSerializer.SerializeToElement("/readings")
        };
        if (qos is not null) configuration["qOs"] = JsonSerializer.Deserialize<JsonElement>(qos);
        graph.LoadJson(JsonSerializer.Serialize(new { Workflows = new { main = new { ros = configuration } } }));
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        var expected = qos is null or "null" ? QosProfile.Default : JsonSerializer.Deserialize<QosProfile>(qos, json)!;
        var bindable = qos?.Contains("Extra") != true;
        if (bindable) expected = RuntimeProfile();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration,
                [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await components.Items.OfType<MudSelect<string>>().Single(field => field.UserAttributes?.GetValueOrDefault("aria-label") as string == "Durability")
                .ValueChanged.InvokeAsync("TransientLocal");
        });
        var key = qos is null ? "Qos" : "qOs";
        var actual = bindable ? RuntimeProfile() : JsonSerializer.Deserialize<QosProfile>(node.Configuration[key], json)!;
        actual.ShouldBe(expected with { Durability = DurabilityPolicy.TransientLocal });
        if (qos?.Contains("Extra") == true)
        {
            node.Configuration[key].GetProperty("Extra").GetProperty("Keep").GetInt32().ShouldBe(42);
            node.Configuration[key].GetProperty("dEpTh").GetInt32().ShouldBe(17);
            node.Configuration[key].GetProperty("Reliability").ValueKind.ShouldBe(JsonValueKind.Number);
        }
        node.Configuration["Node"].GetString().ShouldBe("missing");
        var after = graph.ToProjectJson(); after.ShouldNotBe(before);
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);

        QosProfile RuntimeProfile()
        {
            using var root = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(
                Encoding.UTF8.GetBytes("{\"Component\":" + JsonSerializer.Serialize(node.Configuration) + "}"))).Build();
            var section = root.GetSection("Component");
            return type == "ros.publisher" ? section.ReadRequiredConfiguration<RosPublisherConfiguration>().CreateOptions().Qos
                : section.ReadRequiredConfiguration<RosSubscriberConfiguration>().CreateOptions().Qos;
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"custom\"")]
    [InlineData("{\"Depth\":1,\"depth\":2}")]
    public async Task ROS_QoS_unusual_shapes_remain_available_in_JSON_without_rewriting(string qos)
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var configuration = new Dictionary<string, JsonElement>
        {
            ["Type"] = JsonSerializer.SerializeToElement("ros.publisher"), ["Qos"] = JsonSerializer.Deserialize<JsonElement>(qos)
        };
        var before = JsonSerializer.Serialize(configuration);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(ComponentConfiguratorComponent.Configuration)] = configuration }));
            view.ToHtmlString().ShouldContain("Use JSON to edit this saved value.");
            JsonSerializer.Serialize(configuration).ShouldBe(before);
        });
    }
}
