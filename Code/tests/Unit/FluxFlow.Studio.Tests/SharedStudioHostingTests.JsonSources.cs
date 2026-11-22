using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
using FluxFlow.Studio.UI.Features.Designer.Pages;
using FluxFlow.Studio.UI.Shared.Configuration;
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
    [InlineData("unchanged")]
    [InlineData("item-cancel")]
    [InlineData("item-unchanged")]
    [InlineData("stale")]
    [InlineData("remove")]
    [InlineData("add")]
    public async Task Source_visual_values_preserve_types_drafts_and_history(string action)
    {
        const string original = """ INLINE: [9007199254740993,1e400,"\u0061",{"x":1,"x":2},true,null] """;
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(JsonSerializer.Serialize(new { Workflows = new { main = new { values = new { Type = "source", vAlUeS = original, Extra = 42 } } } }));
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        var before = graph.ToProjectJson();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
        var opened = new List<IDialogReference>();
        dialogs.DialogInstanceAddedAsync += reference => { opened.Add(reference); reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        Task? edit = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            edit = components.Items.OfType<OptionEditorField>().Single(field => field.Option.Name == "Values").OpenEditorAsync();
        });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var save = components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary && button.Variant == Variant.Filled);
            MudIconButton Action(string label) => components.Items.OfType<MudIconButton>().Last(button => Equals(button.UserAttributes?.GetValueOrDefault("aria-label"), label));
            if (action == "remove") await Action("Remove value 6").OnClick.InvokeAsync(new MouseEventArgs());
            else if (action != "unchanged")
            {
                var itemEdit = Action(action == "add" ? "Add value" : action == "item-unchanged" ? "Edit value 3" : "Edit value 1").OnClick.InvokeAsync(new MouseEventArgs());
                var itemSave = components.Items.OfType<MudButton>().Last(button => button.Color == Color.Primary && button.Variant == Variant.Filled);
                if (action == "item-cancel") opened.Last().Close(DialogResult.Cancel());
                else if (action is not ("add" or "item-unchanged"))
                {
                    var input = components.Items.OfType<MudTextField<string>>().Last(field => field.Label == "Value");
                    await input.ValueChanged.InvokeAsync("NaN");
                    await itemSave.OnClick.InvokeAsync(new MouseEventArgs());
                    opened.Last().Result.IsCompleted.ShouldBeFalse(); graph.ToProjectJson().ShouldBe(before);
                    await input.ValueChanged.InvokeAsync("9007199254740995");
                }
                if (action != "item-cancel") await itemSave.OnClick.InvokeAsync(new MouseEventArgs());
                await itemEdit;
            }
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            if (action == "cancel") { opened[0].Close(DialogResult.Cancel()); return; }
            if (action == "stale") { graph.AddEnvironment("Changed"); graph.Undo(); }
            await save.OnClick.InvokeAsync(new MouseEventArgs());
        });
        await edit!;
        if (action is "cancel" or "unchanged" or "stale" or "item-cancel" or "item-unchanged")
        { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); return; }
        var saved = graph.SelectedNode!.Configuration["vAlUeS"].GetString()!;
        using var json = JsonDocument.Parse(JsonSourceValue.Parse(saved).Content);
        var items = json.RootElement.EnumerateArray().ToArray();
        items[0].GetRawText().ShouldBe(action == "save" ? "9007199254740995" : "9007199254740993");
        items[1].GetRawText().ShouldBe("1e400"); items[2].GetString().ShouldBe("a");
        items[3].EnumerateObject().Count().ShouldBe(2); items[4].GetBoolean().ShouldBeTrue();
        items.Length.ShouldBe(action == "remove" ? 5 : action == "add" ? 7 : 6);
        if (action == "add") items[6].GetString().ShouldBe("");
        graph.SelectedNode.Configuration["Extra"].GetInt32().ShouldBe(42);
        var after = graph.ToProjectJson(); graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after); graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Theory]
    [InlineData("source", "Values", "save")]
    [InlineData("source", "Values", "cancel")]
    [InlineData("source", "Values", "unchanged")]
    [InlineData("source", "Values", "stale")]
    [InlineData("reducer", "InitialState", "save")]
    [InlineData("reducer", "InitialState", "cancel")]
    [InlineData("reducer", "InitialState", "unchanged")]
    [InlineData("reducer", "InitialState", "stale")]
    public async Task JSON_source_properties_preserve_configuration_and_history(string type, string property, string action)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"item":{"Type":"TYPE","Extra":42}}}}""".Replace("TYPE", type));
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var key = property.ToUpperInvariant(); const string original = " FILE:original.json ";
        node.Configuration[key] = JsonSerializer.SerializeToElement(original);
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var option = scope.ServiceProvider.GetServices<IComponentDescription>().Single(d => d.Palette.ComponentType == type).Inspector
            .Sections.SelectMany(s => s.Options).Single(o => o.Name == property);
        option.Editor.ShouldBe(OptionEditorKind.JsonSource); option.JsonArrayOnly.ShouldBe(type == "source");
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            if (action == "stale") { graph.AddEnvironment("Changed"); graph.Undo(); }
            reference.Dismiss(action == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(action == "unchanged" ? original : "inline:[1,\"1\",null]"));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            await components.Items.OfType<OptionEditorField>().Single().OpenEditorAsync();
        });
        if (action == "save")
        {
            node.Configuration[key].GetString().ShouldBe("inline:[1,\"1\",null]");
            node.Configuration["Extra"].GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson(); graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after); graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        }
        else { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); }
    }

    [Fact]
    public async Task JSON_source_dialog_retains_both_drafts_when_switching_modes_and_does_not_save_invalid_arrays()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>(); await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialog = await dialogs.ShowAsync<JsonSourceDialog>("Values", new DialogParameters<JsonSourceDialog>
                { { d => d.Value, "inline:{\"value\":1}" }, { d => d.ArrayOnly, true } });
            await components.Items.OfType<MudButton>().Single(b => b.ChildContent is not null && b.Color == Color.Primary).OnClick.InvokeAsync(new MouseEventArgs());
            dialog.Result!.IsCompleted.ShouldBeFalse();
            var mode = components.Items.OfType<MudSelect<bool>>().Single();
            await mode.ValueChanged.InvokeAsync(true);
            var path = components.Items.OfType<MudTextField<string>>().Single();
            await path.ValueChanged.InvokeAsync("123");
            await mode.ValueChanged.InvokeAsync(false);
            components.Items.OfType<ExpressionCodeEditor>().Last().Value.ShouldBe("{\"value\":1}");
            await mode.ValueChanged.InvokeAsync(true);
            await components.Items.OfType<MudButton>().Single(b => b.ChildContent is not null && b.Color == Color.Primary).OnClick.InvokeAsync(new MouseEventArgs());
            (await dialog.Result)!.Data.ShouldBe("file:123");
        });
    }
}
