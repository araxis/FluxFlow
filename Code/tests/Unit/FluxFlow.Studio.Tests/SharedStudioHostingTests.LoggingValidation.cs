using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Studio.UI.Features.Connections;

using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Pages;
using FluxFlow.Studio.UI.Features.Observations.Validation;
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
    [InlineData(null)]
    [InlineData("saved.category")]
    [InlineData("   ")]
    public async Task Logger_category_preserves_invalid_saved_text_until_explicit_repair(string? saved)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"log":{"Type":"logger","Future":42}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        if (saved is not null) node.Configuration["category"] = JsonSerializer.SerializeToElement(saved);
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            MudTextField<string> Field() => components.Items.OfType<MudTextField<string>>().Last(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Category"));
            var field = Field();
            await field.ValueChanged.InvokeAsync("\t ");
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); diagnostics.Messages.Count.ShouldBe(1);
            await field.ValueChanged.InvokeAsync(" Exact.Category ");
            await Task.Yield();
            graph.SelectedNode!.Configuration[saved is null ? "Category" : "category"].GetString().ShouldBe(" Exact.Category ");
            diagnostics.Messages.ShouldBeEmpty();
            var changed = graph.ToProjectJson();
            await Field().ValueChanged.InvokeAsync("");
            await Task.Yield();
            graph.SelectedNode!.Configuration.Keys.ShouldNotContain(key => key.Equals("Category", StringComparison.OrdinalIgnoreCase));
            graph.Undo(); graph.ToProjectJson().ShouldBe(changed);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo();
            var cleared = graph.ToProjectJson(); graph.LoadProject(cleared); graph.ToProjectJson().ShouldBe(cleared);
            graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration["Future"].GetInt32().ShouldBe(42);
        });
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("""{"Protocol":1,"MinimumLogLevel":3}""", true)]
    [InlineData("""{"Headers":"X-Test=first,X-Test=second"}""", true)]
    [InlineData("""{"Headers":"Authorization=Bearer%20sample==,X-Test=a%2Cb"}""", true)]
    [InlineData("""{"Endpoint":"ftp://example.test"}""", false)]
    [InlineData("""{"Endpoint":"https://user:secret@example.test"}""", false)]
    [InlineData("""{"Protocol":42}""", false)]
    [InlineData("""{"MinimumLogLevel":"unknown-secret"}""", false)]
    [InlineData("""{"ExportTimeout":"00:00:00"}""", false)]
    [InlineData("""{"ExportTimeout":"24.20:31:23.648"}""", false)]
    [InlineData("""{"Headers":"invalid-secret"}""", false)]
    [InlineData("""{"Headers":"Authorization=first,Authorization=second-secret"}""", false)]
    [InlineData("""{"Headers":"Bad name=secret"}""", false)]
    public void OTLP_JSON_validation_matches_native_registration_without_creating_an_exporter(string patch, bool valid)
    {
        using var provider = CreateServices().BuildServiceProvider();
        var description = provider.GetServices<ConnectionDescription>().Single(connection => connection.Type == "logging.otlp"); var value = description.Create();
        foreach (var pair in JsonNode.Parse(patch)!.AsObject()) value[pair.Key] = pair.Value?.DeepClone();
        var original = value.ToJsonString();
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(
            new JsonObject { ["Resource"] = value.DeepClone() }.ToJsonString()))).Build();
        using var native = new ServiceCollection().AddLogger().BuildServiceProvider();
        var registration = native.GetRequiredKeyedService<ResourceRegistration>("logging.otlp");
        var nativeError = Record.Exception(() => registration(new ServiceCollection(), ApplicationAddress.Resource("collector"), configuration.GetSection("Resource")));
        var editorError = Record.Exception(() => description.Validate!(value));
        (nativeError is null).ShouldBe(valid); (editorError is null).ShouldBe(valid);
        if (editorError is not null) editorError.Message.ShouldNotContain("secret");
        value.ToJsonString().ShouldBe(original);
    }

    [Theory]
    [InlineData("""{"Type":"logging.otlp","Endpoint":"ftp://example.test"}""", true)]
    [InlineData("[]", true)]
    [InlineData("", true)]
    [InlineData("""{"Type":"http.client","Endpoint":"https://example.test"}""", true)]
    [InlineData("""{"Type":"logging.otlp","ExportTimeout":"00:00:00","Endpoint":"https://example.test"}""", false)]
    public async Task Connection_JSON_validation_keeps_invalid_draft_open_and_preserves_history(string invalid, bool cancel)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"collector":{"Type":"logging.otlp","Endpoint":"https://example.test","Headers":"X-Saved=original=="}},"Workflows":{"main":{}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo(); var before = graph.ToProjectJson();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>(); IDialogReference? opened = null;
        dialogs.DialogInstanceAddedAsync += reference => { opened = reference; reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task? edit = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<MudDialogProvider>();
            await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:collector" }));
            var button = components.Items.OfType<MudButton>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "Advanced connection JSON"));
            edit = button.OnClick.InvokeAsync(new MouseEventArgs());
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var editor = components.Items.OfType<TextEditorDialog>().Single(); editor.ValidateJson.ShouldNotBeNull();
            // Static rendering uses the editor's initial text; replace it before the Save callback.
            await components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionCodeEditor>().Last()
                .SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = invalid }));
            var save = components.Items.OfType<MudButton>().Single(item => item.Variant == Variant.Filled);
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            opened!.Result.IsCompleted.ShouldBeFalse();
            editor.Value.ShouldBe(invalid); components.Items.OfType<MudAlert>().ShouldNotBeEmpty();
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            if (cancel) opened.Close(DialogResult.Cancel());
            else
            {
                var corrected = graph.Connections.Single().Settings.DeepClone().AsObject();
                corrected["Endpoint"] = "https://repaired.test";
                await components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Expressions.ExpressionCodeEditor>().Last()
                    .SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = corrected.ToJsonString() }));
                await save.OnClick.InvokeAsync(new MouseEventArgs());
            }
        });
        await edit!;
        if (cancel) { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); }
        else
        {
            graph.Connections.Single().Settings["Endpoint"]!.GetValue<string>().ShouldBe("https://repaired.test");
            graph.Connections.Single().Settings["Headers"]!.GetValue<string>().ShouldBe("X-Saved=original==");
            var after = graph.ToProjectJson(); graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        }
    }
}
