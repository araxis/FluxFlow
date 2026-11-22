using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
using FluxFlow.Plugins.Mqtt;
using FluxFlow.Studio.UI.Features.Plugins;
using FluxFlow.Studio.UI.Features.Designer.Pages;
using FluxFlow.Studio.UI.Features.Observations.Validation;
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
    [InlineData("save")]
    [InlineData("cancel")]
    [InlineData("no-op")]
    [InlineData("clear")]
    [InlineData("stale")]
    public async Task MQTT_topics_JSON_retains_invalid_drafts_and_preserves_history(string action)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        const string original = """{"topic":"saved/#","qos":"1"}""";
        graph.LoadJson("""{"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"Resources.broker","tOpIcS":TOPICS,"AcknowledgementMode":"Manual","MaxPendingAcknowledgements":7,"Future":42}}}}""".Replace("TOPICS", original));
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        var before = graph.ToProjectJson();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>(); IDialogReference? opened = null;
        dialogs.DialogInstanceAddedAsync += reference => { opened = reference; reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task? edit = null;
        Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent root = default;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            root = await renderer.RenderComponentAsync<DesignerPage>();
            edit = components.Items.OfType<MudButton>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Edit Topics"))
                .OnClick.InvokeAsync(new MouseEventArgs());
        });
        const string corrected = """[{"topic":"saved/#","qos":"1"},{"Topic":"saved/#","QoS":1}]""";
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var editor = components.Items.OfType<ExpressionCodeEditor>().Last();
            var save = components.Items.OfType<MudButton>().Single(item => item.Variant == Variant.Filled);
            foreach (var invalid in new[] { "{", "[]", "[\"work/#\"]", """{"Topic":"work/#","QoS":2}""", """{"Topic":"work/#/bad","QoS":1}""", """{"Topic":"work","QoS":1,"Unknown":true}""", """{"Topic":"work","QoS":1,"qos":2}""" })
            {
                await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = invalid }));
                await save.OnClick.InvokeAsync(new MouseEventArgs());
                opened!.Result.IsCompleted.ShouldBeFalse(); graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
                if (invalid.Contains("work/#/bad", StringComparison.Ordinal))
                    root.ToHtmlString().ShouldContain("Workflows:main:receive:Topics");
            }
            if (action == "cancel") { opened!.Close(DialogResult.Cancel()); return; }
            if (action == "stale") { graph.AddEnvironment("Changed while editing"); graph.Undo(); }
            await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["Value"] = action == "clear" ? "" : action == "no-op" ? original : corrected }));
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            opened!.Result.IsCompleted.ShouldBeTrue();
        });
        await edit!;
        if (action is "cancel" or "no-op" or "stale")
        { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); return; }
        var saved = graph.SelectedNode!.Configuration;
        saved.ContainsKey("Topics").ShouldBeFalse(); saved["Future"].GetInt32().ShouldBe(42);
        saved["Connection"].GetString().ShouldBe("Resources.broker"); saved["MaxPendingAcknowledgements"].GetInt32().ShouldBe(7);
        if (action == "clear") saved.ContainsKey("tOpIcS").ShouldBeFalse();
        else JsonNode.DeepEquals(JsonNode.Parse(saved["tOpIcS"].GetRawText()), JsonNode.Parse(corrected)).ShouldBeTrue();
        var after = graph.ToProjectJson(); graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after); graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MQTT_topic_dialog_checks_manual_QoS_before_committing(bool cancel)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"Resources.broker","Topics":{"Topic":"work","QoS":2},"AcknowledgementMode":"Manual","MaxPendingAcknowledgements":7,"Future":42}}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        var before = graph.ToProjectJson();
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>(); IDialogReference? opened = null;
        dialogs.DialogInstanceAddedAsync += reference => { opened = reference; reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task? edit = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            diagnostics.Messages.Values.ShouldContain(message => message.Path == "Topics" && message.Message.Contains("Manual acknowledgement requires QoS 1"));
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            edit = components.Items.OfType<MudIconButton>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Edit topic 1: work"))
                .OnClick.InvokeAsync(new MouseEventArgs());
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var save = components.Items.OfType<MudButton>().Single(item => item.Variant == Variant.Filled);
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            opened!.Result.IsCompleted.ShouldBeFalse(); graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            components.Items.OfType<MudSelect<string>>().Single(field => field.Label == "QoS").Error.ShouldBeTrue();
            if (cancel) { opened.Close(DialogResult.Cancel()); return; }
            await components.Items.OfType<MudSelect<string>>().Single(field => field.Label == "QoS").ValueChanged.InvokeAsync("1");
            await save.OnClick.InvokeAsync(new MouseEventArgs());
        });
        await edit!;
        if (cancel) { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); return; }
        MqttTopics.QoS(new MqttTopics().Read(graph.SelectedNode!.Configuration).Single()).ShouldBe(1);
        diagnostics.Messages.Values.ShouldNotContain(message => message.Path == "Topics");
        var after = graph.ToProjectJson(); graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after); graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public async Task MQTT_topics_diagnostic_tracks_mode_and_selection_without_rewriting_subscriptions()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"Resources.broker","Topics":{"Topic":"work","QoS":2},"AcknowledgementMode":"Manual","MaxPendingAcknowledgements":7},"other":{"Type":"logger"}}}}""");
        graph.ResetHistory();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "receive"), true);
        var before = graph.ToProjectJson();
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            var issue = diagnostics.Messages.Single(item => item.Value.Path == "Topics");
            issue.Value.Address!.ComponentName.ShouldBe("receive");
            await components.Items.OfType<MudIconButton>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Show validation for Topics"))
                .OnClick.InvokeAsync(new MouseEventArgs());
            diagnostics.Selected.ShouldBe(issue.Key);
            await components.Items.OfType<MudSelect<string>>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Acknowledgement"))
                .ValueChanged.InvokeAsync("Automatic");
        });
        diagnostics.Messages.Values.ShouldNotContain(message => message.Path == "Topics");
        graph.SelectedNode!.Configuration["Topics"].GetProperty("QoS").GetInt32().ShouldBe(2);
        graph.SelectedNode.Configuration["MaxPendingAcknowledgements"].GetInt32().ShouldBe(7);
        var after = graph.ToProjectJson();
        await renderer.Dispatcher.InvokeAsync(() => graph.Undo());
        graph.ToProjectJson().ShouldBe(before);
        diagnostics.Messages.Values.ShouldContain(message => message.Path == "Topics");
        await renderer.Dispatcher.InvokeAsync(() => graph.Redo());
        graph.ToProjectJson().ShouldBe(after);
        diagnostics.Messages.Values.ShouldNotContain(message => message.Path == "Topics");
        await renderer.Dispatcher.InvokeAsync(() => graph.Undo());
        await renderer.Dispatcher.InvokeAsync(() => graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "other"), true));
        diagnostics.Messages.Values.ShouldNotContain(message => message.Path == "Topics");
        graph.ToProjectJson().ShouldBe(before);
    }
}
