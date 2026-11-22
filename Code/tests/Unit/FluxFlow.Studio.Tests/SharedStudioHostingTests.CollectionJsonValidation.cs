using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
using FluxFlow.Studio.UI.Features.Designer.Pages;
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
    public static IEnumerable<object[]> CollectionJsonCases =>
        from type in new[] { "logging.otlp", "http.client", "http.trigger" }
        from action in new[] { "cancel", "save", "no-op", "clear", "stale" }
        select new object[] { type, action };

    [Theory]
    [MemberData(nameof(CollectionJsonCases))]
    public async Task Collection_JSON_keeps_invalid_drafts_and_preserves_native_values_and_history(string type, string action)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var methods = type == "http.trigger";
        var original = type switch
        {
            "logging.otlp" => "\" X-Saved = original== ,X-Other=a%2cb\"",
            "http.client" => """{"X-Saved":"original=="}""",
            _ => """["CUSTOM","GET","CUSTOM"]"""
        };
        var key = methods ? "mEtHoDs" : "hEaDeRs";
        var settings = JsonNode.Parse($$"""{"Type":"{{type}}","{{key}}":{{original}},"Future":42}""")!.AsObject();
        if (!methods) settings[type == "http.client" ? "BaseAddress" : "Endpoint"] = "https://example.test";
        var project = methods
            ? new JsonObject { ["Workflows"] = new JsonObject { ["main"] = new JsonObject { ["item"] = settings } } }
            : new JsonObject { ["Resources"] = new JsonObject { ["connection"] = settings }, ["Workflows"] = new JsonObject { ["main"] = new JsonObject() } };
        graph.LoadJson(project.ToJsonString()); graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        if (methods) graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        var before = graph.ToProjectJson();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>(); IDialogReference? opened = null;
        dialogs.DialogInstanceAddedAsync += reference => { opened = reference; reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task? edit = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            if (methods) await renderer.RenderComponentAsync<DesignerPage>();
            else
            {
                await renderer.RenderComponentAsync<MudPopoverProvider>(); await renderer.RenderComponentAsync<MudDialogProvider>();
                await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:connection" }));
            }
            var button = components.Items.OfType<MudButton>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), methods ? "Edit Methods" : "Edit Headers"));
            edit = button.OnClick.InvokeAsync(new MouseEventArgs());
        });
        var corrected = type switch
        {
            "logging.otlp" => "\" X-Saved = original== ,X-Test=first,X-Test=second\"",
            "http.client" => """{"X-Saved":"original==","X-New":""}""",
            _ => """["CUSTOM","GET","CUSTOM","PATCH"]"""
        };
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var editor = components.Items.OfType<ExpressionCodeEditor>().Last();
            var save = components.Items.OfType<MudButton>().Single(item => item.Variant == Variant.Filled);
            var invalids = type switch
            {
                "logging.otlp" => new[] { "{}", "\"Bad name=private-secret\"", "\"Authorization=first,Authorization=private-secret\"" },
                "http.client" => new[] { "[]", """{"X-Test":3}""", """{"Bad name":"private-secret"}""" },
                _ => new[] { "{}", "[3]", "[\"bad method\"]" }
            };
            foreach (var invalid in invalids)
            {
                await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = invalid }));
                await save.OnClick.InvokeAsync(new MouseEventArgs());
                opened!.Result.IsCompleted.ShouldBeFalse();
                components.Items.OfType<TextEditorDialog>().Last().Value.ShouldBe(invalid);
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            }
            if (action == "cancel") { opened!.Close(DialogResult.Cancel()); return; }
            if (action == "stale") { graph.AddEnvironment("Changed while editing"); graph.Undo(); }
            await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["Value"] = action == "no-op" ? original : action == "clear" ? "" : corrected }));
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            opened!.Result.IsCompleted.ShouldBeTrue();
        });
        await edit!;
        if (action is "cancel" or "no-op" or "stale")
        { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); return; }
        var updated = methods ? JsonSerializer.SerializeToNode(graph.SelectedNode!.Configuration)!.AsObject() : graph.Connections.Single().Settings;
        updated["Future"]!.GetValue<int>().ShouldBe(42);
        if (action == "clear") updated.ContainsKey(key).ShouldBeFalse();
        else JsonNode.DeepEquals(updated[key], JsonNode.Parse(corrected)).ShouldBeTrue();
        updated.ContainsKey(methods ? "Methods" : "Headers").ShouldBeFalse();
        var after = graph.ToProjectJson(); graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }
}
