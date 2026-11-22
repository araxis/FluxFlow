using System.Text.Json;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Plugins;
using FluxFlow.Studio.UI.Features.Plugins.NamedOutputs;
using FluxFlow.Studio.UI.Shared.Configuration;
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
    [Theory]
    [InlineData(null, true, false)]
    [InlineData("\"Reply\"", false, true)]
    [InlineData("1", false, true)]
    [InlineData("\" 01 \"", false, true)]
    [InlineData("\"Acknowledge\"", true, false)]
    [InlineData("\"unknown\"", true, true)]
    [InlineData("null", true, true)]
    public void HTTP_response_conditions_do_not_discard_hidden_or_unknown_settings(string? mode, bool status, bool timeout)
    {
        var configuration = new Dictionary<string, JsonElement>
        {
            ["AcknowledgementStatusCode"] = JsonSerializer.SerializeToElement(203),
            ["ReplyTimeout"] = JsonSerializer.SerializeToElement("00:00:37")
        };
        if (mode is not null) configuration["rEsPoNsEmOdE"] = JsonSerializer.Deserialize<JsonElement>(mode);
        var before = JsonSerializer.Serialize(configuration);
        var description = new FluxFlow.Plugins.Http.HttpPlugin().Description.Components.Single(c => c.Palette.ComponentType == "http.trigger");
        var names = description.GetInspector(configuration).Sections.SelectMany(section => section.Options).Select(option => option.Name).ToArray();
        names.Contains("AcknowledgementStatusCode").ShouldBe(status);
        names.Contains("ReplyTimeout").ShouldBe(timeout);
        names.Contains("MaxPendingRequests").ShouldBe(timeout);
        names.Contains("ShutdownMode").ShouldBeTrue();
        JsonSerializer.Serialize(configuration).ShouldBe(before);
    }

    [Theory]
    [InlineData("Output", "true", false)]
    [InlineData("Hot", "true", false)]
    [InlineData("Hot:Room", "true", false)]
    [InlineData("Warm", "", false)]
    [InlineData("Warm", "file:temperature.jsonata", true)]
    public async Task Route_dialog_validates_names_and_preserves_expression_source(string name, string expression, bool valid)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await dialogs.ShowAsync<NamedOutputDialog>("Add route", new DialogParameters<NamedOutputDialog>
            {
                { item => item.Name, name }, { item => item.Expression, expression },
                { item => item.UnavailableNames, new[] { "Output", "Hot" } }
            });
            await components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary).OnClick.InvokeAsync(new MouseEventArgs());
            if (valid)
            {
                var result = (await dialog.Result)!.Data.ShouldBeOfType<NamedOutputEdit>();
                result.ShouldBe(new NamedOutputEdit(name, expression));
            }
            else
            {
                dialog.Result!.IsCompleted.ShouldBeFalse();
                components.Items.OfType<MudTextField<string>>().Single(field => field.Label == "Expression").GetState(field => field.Error).ShouldBeTrue();
                dialog.Close(DialogResult.Cancel());
            }
        });
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("CUSTOM-METHOD", true)]
    [InlineData("bad method", false)]
    [InlineData("GET\r\n", false)]
    public async Task HTTP_method_dialog_uses_existing_protocol_validation(string method, bool valid)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>(); await renderer.RenderComponentAsync<MudDialogProvider>();
            await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(ComponentConfiguratorComponent.Configuration)] = new Dictionary<string, JsonElement> { ["Type"] = JsonSerializer.SerializeToElement("http.trigger") } }));
            var collection = components.Items.OfType<StringCollectionPropertyGrid>().Single();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await dialogs.ShowAsync<StringCollectionEntryDialog>("Add method", new DialogParameters<StringCollectionEntryDialog>
            { { item => item.Value, method }, { item => item.Label, "Method" }, { item => item.Validate, collection.Validate } });
            await components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary).OnClick.InvokeAsync(new MouseEventArgs());
            if (valid) (await dialog.Result)!.Data.ShouldBe(method);
            else
            {
                dialog.Result!.IsCompleted.ShouldBeFalse();
                components.Items.OfType<MudTextField<string>>().Single(field => field.Label == "Method").GetState(field => field.Error).ShouldBeTrue();
                dialog.Close(DialogResult.Cancel());
            }
        });
    }

    [Theory]
    [InlineData("add")]
    [InlineData("edit")]
    [InlineData("remove")]
    [InlineData("cancel")]
    [InlineData("stale")]
    [InlineData("no-op")]
    public async Task HTTP_methods_collection_preserves_spelling_custom_methods_and_history(string action)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"trigger":{"Type":"http.trigger","Route":"/test","mEtHoDs":["GET","CUSTOM"],"Extra":{"Keep":42}}}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            if (action == "stale") graph.AddEnvironment("Changed");
            reference.Dismiss(action == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(action == "no-op" ? "GET" : "PATCH"));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration,
                [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            graph.ToProjectJson().ShouldBe(before);
            var label = action == "add" ? "Add method" : action == "remove" ? "Remove method 1: GET" : "Edit method 1: GET";
            await components.Items.OfType<MudIconButton>().Single(button => button.UserAttributes?.GetValueOrDefault("aria-label") as string == label).OnClick.InvokeAsync(new MouseEventArgs());
            var methods = node.Configuration["mEtHoDs"].EnumerateArray().Select(value => value.GetString()).ToArray();
            methods.ShouldBe(action switch { "add" => ["GET", "CUSTOM", "PATCH"], "edit" => ["PATCH", "CUSTOM"], "remove" => ["CUSTOM"], _ => ["GET", "CUSTOM"] });
            node.Configuration.ContainsKey("Methods").ShouldBeFalse();
            node.Configuration["Extra"].GetProperty("Keep").GetInt32().ShouldBe(42);
            if (action is "add" or "edit" or "remove")
            {
                var after = graph.ToProjectJson();
                graph.Undo(); graph.ToProjectJson().ShouldBe(before);
                graph.Redo(); graph.ToProjectJson().ShouldBe(after);
                graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
            }
            else if (action != "stale") { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); }
        });
    }

    [Theory]
    [InlineData(null, "POST", true)]
    [InlineData("[]", "No entries", true)]
    [InlineData("null", "Use JSON", false)]
    [InlineData("[\"GET\",42]", "Use JSON", false)]
    [InlineData("\"GET\"", "Use JSON", false)]
    public async Task HTTP_method_defaults_and_unusual_saved_values_are_not_rewritten(string? saved, string expected, bool canAdd)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"trigger":{"Type":"http.trigger","Route":"/test"}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        if (saved is not null) node.Configuration["Methods"] = JsonSerializer.Deserialize<JsonElement>(saved);
        graph.ResetHistory(); var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration }));
            view.ToHtmlString().ShouldContain(expected);
            components.Items.OfType<MudIconButton>().Single(button => button.UserAttributes?.GetValueOrDefault("aria-label") as string == "Add method").Disabled.ShouldBe(!canAdd);
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData("add")]
    [InlineData("edit")]
    [InlineData("remove")]
    [InlineData("cancel")]
    [InlineData("stale")]
    [InlineData("no-op")]
    public async Task Router_collection_preserves_sources_wiring_and_undo(string action)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"router":{"Type":"router","Engine":"jsonata","rOuTeS":{"Hot":"file:hot.jsonata","Cold":"base64:ZmFsc2U="},"Hot":"sink.Input","Extra":{"Keep":42}},"sink":{"Type":"logger"}}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "router");
        graph.Diagram.SelectModel(node, true); var before = graph.ToProjectJson();
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            if (action == "stale") graph.AddEnvironment("Changed");
            reference.Dismiss(action == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(action == "remove" ? (object)true
                : new NamedOutputEdit(action == "add" ? "Warm" : "Hot", action == "no-op" ? "file:hot.jsonata" : "Reading > 20")));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<NamedOutputConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration,
                [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var label = action == "add" ? "Add route" : action == "remove" ? "Remove route Hot" : "Edit route Hot";
            await components.Items.OfType<MudIconButton>().Single(button => button.UserAttributes?.GetValueOrDefault("aria-label") as string == label).OnClick.InvokeAsync(new MouseEventArgs());
            var routes = node.Configuration["rOuTeS"];
            routes.GetProperty("Cold").GetString().ShouldBe("base64:ZmFsc2U=");
            node.Configuration["Hot"].GetString().ShouldBe("sink.Input");
            node.Configuration["Extra"].GetProperty("Keep").GetInt32().ShouldBe(42);
            node.Configuration.ContainsKey("Routes").ShouldBeFalse();
            if (action == "add") routes.GetProperty("Warm").GetString().ShouldBe("Reading > 20");
            if (action == "remove") routes.TryGetProperty("Hot", out _).ShouldBeFalse();
            else routes.GetProperty("Hot").GetString().ShouldBe(action == "edit" ? "Reading > 20" : "file:hot.jsonata");
            if (action is "add" or "edit" or "remove")
            {
                var after = graph.ToProjectJson();
                graph.Undo(); graph.ToProjectJson().ShouldBe(before);
                graph.Redo(); graph.ToProjectJson().ShouldBe(after);
                graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
            }
            else if (action != "stale") { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); }
        });
    }
}
