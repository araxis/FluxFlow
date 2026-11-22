using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Plugins.Mqtt;
using FluxFlow.Studio.UI.Features.Plugins;
using FluxFlow.Studio.UI.Features.Designer.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Theory]
    [InlineData("null", "Manual", false)]
    [InlineData("{}", "Automatic", false)]
    [InlineData("{}", "Manual", false)]
    [InlineData("null", "Manual", true)]
    public async Task MQTT_empty_topics_match_registration_and_visual_add_preserves_configuration(string topics, string mode, bool cancel)
    {
        var json = """{"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"Resources.broker","tOpIcS":TOPICS,"AcknowledgementMode":"MODE","MaxPendingAcknowledgements":7}}}}"""
            .Replace("TOPICS", topics).Replace("MODE", mode);
        using var nativeConfiguration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        using var native = new ServiceCollection().AddMqtt().BuildServiceProvider();
        // Registration validates the subscription shape without constructing a client or connecting.
        native.GetRequiredKeyedService<ComponentRegistration>("mqtt.subscriber")(
            new ServiceCollection(), nativeConfiguration.GetSection("Workflows:main:receive"));

        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var project = JsonNode.Parse(json)!;
        project["Workflows"]!["main"]!["receive"]!["Future"] = 42;
        graph.LoadJson(project.ToJsonString()); graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        var before = graph.ToProjectJson();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>(); IDialogReference? opened = null;
        dialogs.DialogInstanceAddedAsync += reference => { opened = reference; reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task? edit = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<DesignerPage>();
            rendered.ToHtmlString().ShouldContain("No topics");
            rendered.ToHtmlString().ShouldNotContain("Invalid topic");
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            edit = components.Items.OfType<PortableCollectionConfigurator>().Single().OpenEditorAsync();
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var dialog = components.Items.OfType<PropertyRowDialog>().Single();
            ((int?)dialog.Fields.Single(f => f.Name == "QoS").DefaultValue).ShouldBe(mode == "Manual" ? 1 : 0);
            var field = components.Items.OfType<MudTextField<string>>().Single(item => item.Label == "Topic filter");
            await field.ValueChanged.InvokeAsync("sensors/#");
            if (cancel) { opened!.Close(DialogResult.Cancel()); return; }
            await components.Items.OfType<MudButton>().Single(item => item.Variant == Variant.Filled).OnClick.InvokeAsync(new MouseEventArgs());
        });
        await edit!;
        if (cancel) { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); return; }
        var saved = graph.SelectedNode!.Configuration;
        var added = new MqttTopics().Read(saved).Single();
        MqttTopics.Topic(added).ShouldBe("sensors/#"); MqttTopics.QoS(added).ShouldBe(mode == "Manual" ? 1 : 0);
        saved.ContainsKey("Topics").ShouldBeFalse(); saved["Future"].GetInt32().ShouldBe(42);
        saved["Connection"].GetString().ShouldBe("Resources.broker"); saved["MaxPendingAcknowledgements"].GetInt32().ShouldBe(7);
        var after = graph.ToProjectJson(); graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public async Task MQTT_removing_last_topic_omits_initial_subscriptions_and_preserves_history()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"Resources.broker","tOpIcS":{"Topic":"sensors/#","QoS":1},"AcknowledgementMode":"Manual","MaxPendingAcknowledgements":7}}}}""");
        graph.ResetHistory(); graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            await components.Items.OfType<MudIconButton>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Remove topic 1: sensors/#"))
                .OnClick.InvokeAsync(new MouseEventArgs());
        });
        graph.SelectedNode!.Configuration.Keys.ShouldNotContain(key => key.Equals("Topics", StringComparison.OrdinalIgnoreCase));
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(graph.ToJson()))).Build();
        using var native = new ServiceCollection().AddMqtt().BuildServiceProvider();
        native.GetRequiredKeyedService<ComponentRegistration>("mqtt.subscriber")(
            new ServiceCollection(), configuration.GetSection("Workflows:main:receive"));
        var after = graph.ToProjectJson(); graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }
}
