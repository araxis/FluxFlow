using FluxFlow.Authoring;
using System.Text.Json;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
using FluxFlow.Plugins.RabbitMQ;
using FluxFlow.Studio.UI.Features.Plugins.ObjectGroups;
using FluxFlow.Studio.UI.Features.Plugins;
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
    [InlineData("{\"Queues\":[null]}")]
    [InlineData("{\"Queues\":[{\"Name\":false}]}")]
    [InlineData("{\"Queues\":[{\"Name\":\"orders\",\"Durable\":\"invalid\"}]}")]
    [InlineData("{\"Queues\":[{\"Name\":\"orders\",\"Exclusive\":1}]}")]
    [InlineData("{\"Exchanges\":[{\"Name\":\"events\",\"AutoDelete\":{}}]}")]
    public async Task Rabbit_declarations_invalid_saved_values_remain_available_for_json_repair(string topology)
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var configuration = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{\"Type\":\"amqp.publisher\",\"Topology\":" + topology + "}")!;
        var before = JsonSerializer.Serialize(configuration);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ObjectGroupsGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(ObjectGroupsGrid.Schema)] = RabbitDeclarationFields.Groups, [nameof(ObjectGroupsGrid.Configuration)] = configuration }));
            view.ToHtmlString().ShouldContain("Use Advanced JSON to repair it");
            JsonSerializer.Serialize(configuration).ShouldBe(before);
        });
    }

    [Fact]
    public async Task Rabbit_declarations_removing_first_item_retains_remaining_values_and_edit_targets()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"send":{"Type":"amqp.publisher","Topology":{"Queues":[{"Name":"first","Durable":true},{"Name":"second","Durable":"false","AutoDelete":"true","Exclusive":true,"Arguments":{"x-message-ttl":{"Type":"Int32","Value":1000}}}]}}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        graph.Diagram.SelectModel(node, true);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<ObjectGroupsGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ObjectGroupsGrid.Schema)] = RabbitDeclarationFields.Groups, [nameof(ObjectGroupsGrid.Configuration)] = node.Configuration,
                [nameof(ObjectGroupsGrid.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            await components.Items.OfType<MudIconButton>().Single(button => button.UserAttributes?["aria-label"]?.ToString() == "Remove queue: first").OnClick.InvokeAsync(new MouseEventArgs());
            var removed = graph.ToProjectJson();
            node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
            await components.Items.OfType<ObjectGroupsGrid>().Single().SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(ObjectGroupsGrid.Schema)] = RabbitDeclarationFields.Groups, [nameof(ObjectGroupsGrid.Configuration)] = node.Configuration }));
            node.Configuration["Topology"].GetProperty("Queues")[0].GetProperty("Durable").GetString().ShouldBe("false");
            var name = components.Items.OfType<MudTextField<string>>().Last(field => field.GetState(field => field.Value) == "second");
            var durable = components.Items.OfType<MudCheckBox<bool>>().Last(field => field.UserAttributes?["aria-label"]?.ToString() == "queue Durable");
            durable.GetState(field => field.Value).ShouldBeFalse();
            components.Items.OfType<MudCheckBox<bool>>().Last(field => field.UserAttributes?["aria-label"]?.ToString() == "queue AutoDelete").GetState(field => field.Value).ShouldBeTrue();
            await name.ValueChanged.InvokeAsync("remaining");
            var edited = graph.ToProjectJson();
            var saved = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration["Topology"].GetProperty("Queues");
            saved.GetArrayLength().ShouldBe(1);
            saved[0].GetProperty("Name").GetString().ShouldBe("remaining");
            saved[0].GetProperty("Arguments").GetProperty("x-message-ttl").GetProperty("Value").GetInt32().ShouldBe(1000);
            graph.Undo(); graph.ToProjectJson().ShouldBe(removed);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo(); graph.ToProjectJson().ShouldBe(edited);
        });
    }

    [Theory]
    [InlineData("amqp.publisher", false)]
    [InlineData("amqp.publisher", true)]
    [InlineData("amqp.consumer", false)]
    [InlineData("amqp.consumer", true)]
    public async Task Rabbit_publisher_advanced_json_validates_and_cancel_keeps_original(string componentType, bool cancel)
    {
        var original = """{"Type":"amqp.publisher","Connection":"broker","Queue":"orders","Future":42,"Output":"log.Input"}""".Replace("amqp.publisher", componentType);
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialog = await scope.ServiceProvider.GetRequiredService<IDialogService>().ShowAsync<StructuredSettingsDialog>("RabbitMQ Publisher JSON",
                new DialogParameters<StructuredSettingsDialog> { { item => item.Value, original }, { item => item.Type, componentType }, { item => item.JsonOnly, true }, { item => item.StartWithJson, true } });
            var editor = components.Items.OfType<ExpressionCodeEditor>().Single();
            var save = components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary);
            await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(editor.Value)] = "{" }));
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            dialog.Result.IsCompleted.ShouldBeFalse();
            var updated = original.Replace("42", "43");
            await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(editor.Value)] = updated }));
            if (cancel)
            {
                await components.Items.OfType<MudButton>().First().OnClick.InvokeAsync(new MouseEventArgs());
                await save.OnClick.InvokeAsync(new MouseEventArgs());
                (await dialog.Result)!.Canceled.ShouldBeTrue();
            }
            else
            {
                await save.OnClick.InvokeAsync(new MouseEventArgs());
                var value = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>((await dialog.Result)!.Data.ShouldBeOfType<string>())!;
                value["Future"].GetInt32().ShouldBe(43);
                value["Output"].GetString().ShouldBe("log.Input");
            }
            components.Items.OfType<StructuredSettingsDialog>().Single().Value.ShouldBe(original);
        });
    }

    [Theory]
    [InlineData("amqp.publisher", "Exchanges", "exchange")]
    [InlineData("amqp.publisher", "Queues", "queue")]
    [InlineData("amqp.publisher", "Bindings", "binding")]
    [InlineData("amqp.consumer", "Exchanges", "exchange")]
    [InlineData("amqp.consumer", "Queues", "queue")]
    [InlineData("amqp.consumer", "Bindings", "binding")]
    public async Task Rabbit_declarations_inline_add_edit_remove_preserve_configuration_and_history(string componentType, string section, string kind)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"send":{"Type":"amqp.publisher","Future":42,"Output":"log.Input","tOpOlOgY":{"qUeUeS":[{"Name":"existing","Arguments":{"x-message-ttl":{"Type":"Int32","Value":1000}}}]}},"log":{"Type":"logger"}}}}""".Replace("amqp.publisher", componentType));
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "send");
        graph.Diagram.SelectModel(node, true);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ObjectGroupsGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ObjectGroupsGrid.Schema)] = RabbitDeclarationFields.Groups, [nameof(ObjectGroupsGrid.Configuration)] = node.Configuration,
                [nameof(ObjectGroupsGrid.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            async Task Refresh()
            {
                node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "send");
                await components.Items.OfType<ObjectGroupsGrid>().Single().SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(ObjectGroupsGrid.Schema)] = RabbitDeclarationFields.Groups, [nameof(ObjectGroupsGrid.Configuration)] = node.Configuration }));
            }
            graph.ToProjectJson().ShouldBe(before);
            await components.Items.OfType<MudIconButton>().Single(button => button.UserAttributes?["aria-label"]?.ToString() == $"Add {kind}").OnClick.InvokeAsync(new MouseEventArgs());
            var added = graph.ToProjectJson();
            await Refresh();
            view.ToHtmlString().ShouldContain(section);
            var fieldName = kind == "binding" ? "Queue" : "Name";
            var field = components.Items.OfType<MudTextField<string>>().Last(field => field.UserAttributes?["aria-label"]?.ToString() == $"{kind} {(kind == "queue" ? 2 : 1)} {fieldName}");
            await field.ValueChanged.InvokeAsync("orders");
            var edited = graph.ToProjectJson();
            await Refresh();
            edited.ShouldNotBe(added);
            node.Configuration.ContainsKey("Topology").ShouldBeFalse();
            node.Configuration["Future"].GetInt32().ShouldBe(42);
            node.Configuration["tOpOlOgY"].GetProperty("qUeUeS")[0].GetProperty("Arguments").GetProperty("x-message-ttl").GetProperty("Value").GetInt32().ShouldBe(1000);
            await components.Items.OfType<MudIconButton>().Last(button => button.UserAttributes?["aria-label"]?.ToString() == $"Remove {kind}: orders").OnClick.InvokeAsync(new MouseEventArgs());
            var removed = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(edited);
            // Old field callbacks must not alter the restored selection.
            await field.ValueChanged.InvokeAsync("stale"); graph.ToProjectJson().ShouldBe(edited);
            graph.Undo(); graph.ToProjectJson().ShouldBe(added);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.Redo(); graph.Redo(); graph.ToProjectJson().ShouldBe(removed);
            graph.LoadProject(removed); graph.ToProjectJson().ShouldBe(removed);
        });
    }

    [Fact]
    public async Task Rabbit_publisher_dialog_save_refreshes_inline_fields_and_is_one_undoable_edit()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"send":{"Type":"amqp.publisher","Connection":"missing","TimeoutSeconds":30,"Output":"log.Input","Future":42},"log":{"Type":"logger"}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "send");
        graph.Diagram.SelectModel(node, true);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PortablePropertyConfigurator.Configuration)] = node.Configuration,
                [nameof(PortablePropertyConfigurator.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var configurator = components.Items.OfType<PortablePropertyConfigurator>().Single();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            IDialogReference? opened = null;
            dialogs.DialogInstanceAddedAsync += reference => { opened = reference; return Task.CompletedTask; };
            var edit = configurator.OpenEditorAsync();
            opened.ShouldNotBeNull();
            var updated = new Dictionary<string, JsonElement>(node.Configuration)
            {
                ["TimeoutSeconds"] = JsonSerializer.SerializeToElement(75),
                ["Topology"] = JsonSerializer.SerializeToElement(new { Queues = new[] { new { Name = "orders" } } })
            };
            opened.Close(DialogResult.Ok(JsonSerializer.Serialize(updated)));
            await edit;
            await configurator.SetParametersAsync(ParameterView.Empty);
            components.Items.OfType<ValidatedTextField>().Last(field => field.Label == "Timeout (s)").Value.ShouldBe("75");
            node.Configuration["Future"].GetInt32().ShouldBe(42);
            node.Configuration["Output"].GetString().ShouldBe("log.Input");
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Fact]
    public async Task Rabbit_publisher_property_grid_preserves_saved_settings_links_and_history()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""
            {"Resources":{"broker":{"Type":"amqp.connection"},"mqtt":{"Type":"mqtt.client"}},"Workflows":{"main":{
              "send":{"Type":"amqp.publisher","cOnNeCtIoN":"missing","timeoutSeconds":47,"Capacity":9,"MaxMessageBytes":4096,
                "Output":"log.Input","Future":{"Keep":true},"Topology":{"Queues":[{"Name":"orders","Arguments":{"x-message-ttl":{"Type":"Int32","Value":1000}}}]}},
              "log":{"Type":"logger"}}}}
            """);
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(item => item.NodeName == "send");
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
                [nameof(ComponentInspector.NodeKey)] = "main.send",
                [nameof(ComponentInspector.NodeName)] = "send",
                [nameof(ComponentInspector.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var html = view.ToHtmlString();
            html.ShouldContain("Advanced JSON");
            html.ShouldContain("Add exchange");
            html.ShouldContain("Add queue");
            html.ShouldContain("Add binding");
            html.ShouldNotContain("Configure RabbitMQ");
            var fields = components.Items.OfType<OptionEditorField>().ToArray();
            fields.Select(field => field.Option.Name).ShouldBe(new[] { "Connection", "TimeoutSeconds", "Capacity", "MaxMessageBytes" });
            var connection = components.Items.OfType<MudSelect<string>>().Single();
            connection.GetState(item => item.Value).ShouldBe("missing");
            connection.GetState(item => item.Error).ShouldBeTrue();
            components.Items.OfType<MudSelectItem<string>>().Select(item => item.Value).ShouldBe(new[] { "Resources.broker", "missing" });
            graph.ToProjectJson().ShouldBe(before);
            graph.CanUndo.ShouldBeFalse();
            var timeout = components.Items.OfType<ValidatedTextField>().Single(field => field.Label == "Timeout (s)");
            timeout.Value.ShouldBe("47");
            await timeout.ValueChanged.InvokeAsync("60");
            node.Configuration["timeoutSeconds"].GetDouble().ShouldBe(60);
            node.Configuration.ContainsKey("TimeoutSeconds").ShouldBeFalse();
            node.Configuration["Topology"].GetRawText().ShouldBe(topology);
            node.Configuration["Output"].GetString().ShouldBe("log.Input");
            node.Configuration["Future"].GetProperty("Keep").GetBoolean().ShouldBeTrue();
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
        var description = provider.GetServices<IComponentDescription>().Single(item => item.Palette.ComponentType == "amqp.publisher");
        description.Palette.Inputs.Single().Summary.ShouldNotBeNull().ShouldContain("Base64 Body");
        description.Documentation.ShouldContain("not fixed Publisher settings");
    }

    [Theory]
    [InlineData("amqp.publisher")]
    [InlineData("amqp.consumer")]
    public async Task Rabbit_declarations_editor_keeps_advanced_json_optional(string componentType)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            var dialog = await dialogs.ShowAsync<StructuredSettingsDialog>("Broker declarations", new DialogParameters<StructuredSettingsDialog>
            {
                { item => item.Value, """{"Type":"amqp.publisher","Connection":"broker","Topology":{"Queues":[{"Name":"orders"}]}}""".Replace("amqp.publisher", componentType) },
                { item => item.Type, componentType }, { item => item.JsonOnly, true }, { item => item.StartWithJson, true }
            });
            var html = view.ToHtmlString();
            components.Items.OfType<ExpressionCodeEditor>().Single().Value.ShouldContain("orders");
            html.ShouldNotContain("Optional broker declarations");
            html.ShouldNotContain("Publish timeout");
            html.ShouldNotContain("Buffer capacity");
            components.Items.OfType<FluxFlow.Studio.UI.Features.Connections.ConnectionSelect>().ShouldBeEmpty();
            dialog.Close(DialogResult.Cancel());
            (await dialog.Result)!.Canceled.ShouldBeTrue();
        });
    }
}
