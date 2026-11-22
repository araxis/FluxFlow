using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Components.RabbitMQ;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Plugins.RabbitMQ;
using FluxFlow.Studio.UI.Features.Plugins.ObjectGroups;
using FluxFlow.Studio.UI.Features.Plugins;
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
    [Fact]
    public async Task Rabbit_declaration_defaults_and_unknown_exchange_type_are_preserved_until_changed()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"send":{"Type":"amqp.publisher","Topology":{"Exchanges":[{"Name":"default","Durable":"true","AutoDelete":"false"},{"Name":"future","Type":"custom.exchange"}],"Bindings":[{"Queue":"orders","Exchange":"events"}]}}}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true); var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ObjectGroupsGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ObjectGroupsGrid.Schema)] = RabbitDeclarationFields.Groups, [nameof(ObjectGroupsGrid.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(ObjectGroupsGrid.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            foreach (var flag in components.Items.OfType<MudCheckBox<bool>>()) await flag.ValueChanged.InvokeAsync(flag.GetState(item => item.Value));
            var types = components.Items.OfType<MudSelect<string>>().ToArray();
            await types[0].ValueChanged.InvokeAsync("direct");
            types[1].GetState(item => item.Value).ShouldBe("custom.exchange");
            components.Items.OfType<MudSelectItem<string>>().Single(item => item.Value == "custom.exchange").Disabled.ShouldBeTrue();
            await components.Items.OfType<MudTextField<string>>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), "binding 1 RoutingKey")).ValueChanged.InvokeAsync("");
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); graph.CanRedo.ShouldBeTrue();
            await types[1].ValueChanged.InvokeAsync("topic");
            graph.SelectedNode!.Configuration["Topology"].GetProperty("Exchanges")[1].GetProperty("Type").GetString().ShouldBe("topic");
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        });
    }

    [Theory]
    [InlineData("amqp.publisher", "TimeoutSeconds", 600)]
    [InlineData("amqp.publisher", "Capacity", int.MaxValue)]
    [InlineData("amqp.publisher", "MaxMessageBytes", int.MaxValue)]
    [InlineData("amqp.consumer", "Prefetch", ushort.MaxValue)]
    [InlineData("amqp.consumer", "Capacity", int.MaxValue)]
    [InlineData("amqp.consumer", "MaxMessageBytes", int.MaxValue)]
    [InlineData("amqp.settlement", "Capacity", int.MaxValue)]
    public async Task Rabbit_numeric_settings_preserve_representation_and_enforce_existing_bounds(string type, string property, int maximum)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var key = property.ToLowerInvariant();
        graph.LoadJson("""{"Workflows":{"main":{"transport":{"Type":"TYPE","PROPERTY":"0007","Extra":{"Keep":42},"ACK":"reply.Output","NACK":"failed.Output"}}}}""".Replace("TYPE", type).Replace("PROPERTY", key));
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var option = node.Description.Inspector.Sections.SelectMany(section => section.Options).Single(option => option.Name == property);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var input = components.Items.OfType<MudTextField<string>>().Single();
            foreach (var invalid in new[] { "0", "-1", "1.5", ((long)maximum + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) })
            {
                await input.ValueChanged.InvokeAsync(invalid);
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            }
            await input.ValueChanged.InvokeAsync("7"); graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await input.ValueChanged.InvokeAsync(maximum.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.Configuration[key].GetInt64().ShouldBe(maximum);
            node.Configuration["ACK"].GetString().ShouldBe("reply.Output"); node.Configuration["NACK"].GetString().ShouldBe("failed.Output");
            node.Configuration["Extra"].GetProperty("Keep").GetInt32().ShouldBe(42);
            var saved = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
            graph.LoadProject(saved); graph.ToProjectJson().ShouldBe(saved);
        });
    }

    [Theory]
    [InlineData("Queues", "Name")]
    [InlineData("Exchanges", "Name")]
    [InlineData("Bindings", "Queue")]
    [InlineData("Bindings", "Exchange")]
    [InlineData("Bindings", "RoutingKey")]
    public async Task Rabbit_declaration_fields_validate_native_utf8_limits_without_rewriting_saved_data(string section, string field)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var declaration = RabbitDeclarationFields.Groups.Lists.Single(list => list.Name == section).Create();
        if (section == "Bindings") { declaration["Queue"] = "orders"; declaration["Exchange"] = "events"; }
        else declaration["Name"] = "events";
        declaration.Remove(field); var key = field.ToLowerInvariant(); declaration[key] = "saved";
        declaration["Arguments"] = JsonNode.Parse("""{"ttl":{"Type":"Int32","Value":1000}}""");
        graph.LoadJson("""{"Workflows":{"main":{"send":{"Type":"amqp.publisher","Topology":{"SECTION":[DECLARATION]}}}}} """.Replace("SECTION", section).Replace("DECLARATION", declaration.ToJsonString()));
        graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true); graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ObjectGroupsGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ObjectGroupsGrid.Schema)] = RabbitDeclarationFields.Groups, [nameof(ObjectGroupsGrid.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(ObjectGroupsGrid.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var label = $"{(section == "Queues" ? "queue" : section == "Exchanges" ? "exchange" : "binding")} 1 {field}";
            var input = components.Items.OfType<MudTextField<string>>().Single(item => Equals(item.UserAttributes?.GetValueOrDefault("aria-label"), label));
            await input.ValueChanged.InvokeAsync(new string('é', 128)); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            if (field != "RoutingKey") { await input.ValueChanged.InvokeAsync(" "); graph.ToProjectJson().ShouldBe(before); }
            var valid = field == "RoutingKey" ? "  " : new string('é', 127) + "a";
            await input.ValueChanged.InvokeAsync(valid);
            var updated = graph.SelectedNode!.Configuration["Topology"].GetProperty(section)[0];
            updated.GetProperty(key).GetString().ShouldBe(valid);
            updated.GetProperty("Arguments").GetProperty("ttl").GetProperty("Value").GetInt32().ShouldBe(1000);
            var saved = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
            graph.LoadProject(saved); graph.ToProjectJson().ShouldBe(saved);
        });
    }

    [Fact]
    public void Rabbit_consumer_queue_validation_matches_the_runtime()
    {
        var plugin = new RabbitMQPlugin();
        var validate = new RabbitDesign().Component(plugin.Description.Components.Single(c => c.Palette.ComponentType == "amqp.consumer")).TextValidators["Queue"];
        validate(new string('é', 128)).ShouldNotBeNull();
        validate(new string('é', 127) + "a").ShouldBeNull();
        validate(" ").ShouldNotBeNull();
        new RabbitConsumerOptions { Queue = new string('é', 127) + "a" }.Validate();
        Should.Throw<ArgumentException>(() => new RabbitConsumerOptions { Queue = new string('é', 128) }.Validate());
    }

    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    public async Task Rabbit_declaration_limit_preserves_saved_excess_and_rejects_add(int count)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var topology = new RabbitTopology { Queues = Enumerable.Range(0, count).Select(index => new RabbitQueue($"queue-{index}")).ToList() };
        RabbitDeclarationFields.Groups.MaximumItems.ShouldBe(256);
        if (count == RabbitDeclarationFields.Groups.MaximumItems) topology.Validate(); else Should.Throw<ArgumentException>(topology.Validate);
        graph.LoadJson("""{"Workflows":{"main":{"send":{"Type":"amqp.publisher","Topology":TOPOLOGY}}}}""".Replace("TOPOLOGY", JsonSerializer.Serialize(topology)));
        graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true); graph.ResetHistory(); var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ObjectGroupsGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ObjectGroupsGrid.Schema)] = RabbitDeclarationFields.Groups, [nameof(ObjectGroupsGrid.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(ObjectGroupsGrid.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            foreach (var button in components.Items.OfType<MudIconButton>().Where(item => item.UserAttributes?["aria-label"]?.ToString() is "Add queue" or "Add exchange" or "Add binding"))
            { button.Disabled.ShouldBeTrue(); await button.OnClick.InvokeAsync(new MouseEventArgs()); }
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            if (count > 256) view.ToHtmlString().ShouldContain("Show validation for Broker declarations");
        });
    }

    [Theory]
    [InlineData("amqp.publisher")]
    [InlineData("amqp.consumer")]
    public async Task Rabbit_component_json_formatting_only_save_preserves_original_and_redo(string type)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"send":{"Type":"TYPE","Connection":"missing","Queue":"orders","Future":{ "Number": 9223372036854775807 },"Topology":{"Queues":[]}}}}}""".Replace("TYPE", type));
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true); var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>(); await renderer.RenderComponentAsync<MudDialogProvider>();
            await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PortablePropertyConfigurator.Configuration)] = node.Configuration,
                [nameof(PortablePropertyConfigurator.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            IDialogReference? opened = null;
            scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
            { opened = reference; reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var edit = components.Items.OfType<PortablePropertyConfigurator>().Single().OpenEditorAsync();
            var reordered = node.Configuration.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
            var formatted = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(reordered, new JsonSerializerOptions { WriteIndented = true }))!;
            opened!.Close(DialogResult.Ok(JsonSerializer.Serialize(formatted))); await edit;
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); graph.CanRedo.ShouldBeTrue();
        });
    }
}
