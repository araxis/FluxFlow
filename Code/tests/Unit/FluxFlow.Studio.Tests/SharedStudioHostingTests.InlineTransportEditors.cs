using System.Text.Json;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    [InlineData("nats.publisher", "save")]
    [InlineData("nats.publisher", "cancel")]
    [InlineData("nats.publisher", "stale")]
    [InlineData("nats.publisher", "no-op")]
    [InlineData("serial.writer", "save")]
    [InlineData("serial.writer", "cancel")]
    [InlineData("serial.writer", "stale")]
    [InlineData("serial.writer", "no-op")]
    public async Task Transport_advanced_JSON_applies_only_current_confirmed_changes(string type, string action)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"transport":{"Type":"TYPE","Connection":"missing","Capacity":64,"Extra":{"Keep":42}}}}}""".Replace("TYPE", type));
        graph.ResetHistory();
        graph.AddEnvironment("Undo me"); graph.Undo();
        graph.CanRedo.ShouldBeTrue();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        graph.Diagram.SelectModel(node, true);
        var original = JsonSerializer.Serialize(node.Configuration);
        var changes = 0;
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            if (action == "stale") graph.AddEnvironment("Changed");
            reference.Dismiss(action == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(
                action == "no-op" ? JsonSerializer.Serialize(node.Configuration, new JsonSerializerOptions { WriteIndented = true }) : original.Replace("64", "96")));
            return Task.CompletedTask;
        };
        var registration = scope.ServiceProvider.GetServices<IComponentConfigurator>().Single(item => item.Component == type);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<DynamicComponent>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DynamicComponent.Type)] = registration.ComponentType,
                [nameof(DynamicComponent.Parameters)] = new Dictionary<string, object>
                {
                    [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration,
                    [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, () => { changes++; graph.RecordEdit(); })
                }
            }));
            await components.Items.OfType<ComponentConfiguratorComponent>().Single().OpenEditorAsync();
            changes.ShouldBe(action == "save" ? 1 : 0);
            node.Configuration["Capacity"].GetInt32().ShouldBe(action == "save" ? 96 : 64);
            node.Configuration["Extra"].GetProperty("Keep").GetInt32().ShouldBe(42);
            if (action == "save")
            {
                graph.Undo();
                JsonSerializer.Serialize(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration).ShouldBe(original);
                graph.Redo();
                graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration["Capacity"].GetInt32().ShouldBe(96);
            }
            else if (action != "stale") graph.CanRedo.ShouldBeTrue();
        });
    }

    [Theory]
    [InlineData("nats.publisher")]
    [InlineData("nats.subscriber")]
    [InlineData("nats.request")]
    [InlineData("nats.reply")]
    [InlineData("nats.jetstream.publisher")]
    [InlineData("nats.jetstream.consumer")]
    [InlineData("nats.jetstream.settlement")]
    [InlineData("serial.reader")]
    [InlineData("serial.writer")]
    public async Task Transport_inspectors_edit_inline_preserving_unknown_fields_and_history(string type)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"transport":{"Type":"TYPE","Connection":"missing","cApAcItY":64,"Custom":{"Keep":42}}}}}""".Replace("TYPE", type));
        graph.ResetHistory();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var before = graph.ToProjectJson();
        var registration = scope.ServiceProvider.GetServices<IComponentConfigurator>().Single(item => item.Component == type);
        registration.RendersPropertyRows.ShouldBeTrue();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<DynamicComponent>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DynamicComponent.Type)] = registration.ComponentType,
                [nameof(DynamicComponent.Parameters)] = new Dictionary<string, object>
                {
                    [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration,
                    [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
                }
            }));
            view.ToHtmlString().ShouldContain("Advanced JSON");
            view.ToHtmlString().ShouldNotContain("Configure NATS");
            view.ToHtmlString().ShouldNotContain("Configure serial frames");
            graph.ToProjectJson().ShouldBe(before);
            var capacity = components.Items.OfType<ValidatedTextField>().Single(field => field.Label == "Buffer size");
            capacity.Value.ShouldBe("64");
            await capacity.ValueChanged.InvokeAsync("96");
            node.Configuration["cApAcItY"].GetDouble().ShouldBe(96);
            node.Configuration.ContainsKey("Capacity").ShouldBeFalse();
            node.Configuration["Custom"].GetProperty("Keep").GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Theory]
    [InlineData("serial.reader", "Framing", "Raw", "FixedLength", "FrameLength", "DelimiterBase64")]
    [InlineData("nats.jetstream.consumer", "CreateStream", "false", "true", "MaxStreamBytes", "AckWaitSeconds")]
    [InlineData("nats.jetstream.consumer", "CreateConsumer", "false", "true", "AckWaitSeconds", "MaxStreamBytes")]
    public async Task Transport_conditional_fields_preserve_hidden_configuration(string type, string toggle,
        string initial, string changed, string revealed, string hidden)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"transport":{"Type":"TYPE","TOGGLE":"INITIAL","REVEALED":27,"HIDDEN":"keep"}}}}"""
            .Replace("TYPE", type).Replace("TOGGLE", toggle).Replace("INITIAL", initial).Replace("REVEALED", revealed).Replace("HIDDEN", hidden));
        graph.ResetHistory();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var registration = scope.ServiceProvider.GetServices<IComponentConfigurator>().Single(item => item.Component == type);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<DynamicComponent>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DynamicComponent.Type)] = registration.ComponentType,
                [nameof(DynamicComponent.Parameters)] = new Dictionary<string, object>
                {
                    [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration,
                    [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
                }
            }));
            var rows = components.Items.OfType<OptionPropertyRows>().Single();
            rows.Sections.SelectMany(section => section.Options).ShouldNotContain(option => option.Name == revealed || option.Name == hidden);
            var field = components.Items.OfType<OptionEditorField>().Single(item => item.Option.Name == toggle);
            if (toggle == "Framing")
                await components.Items.OfType<MudSelect<string>>().Single(item => item.GetState(field => field.Value) == initial).ValueChanged.InvokeAsync(changed);
            else
                await components.Items.OfType<MudCheckBox<bool>>().Single(item => item.UserAttributes?.GetValueOrDefault("aria-label") as string == field.Option.DisplayName).ValueChanged.InvokeAsync(true);
            await components.Items.OfType<ComponentConfiguratorComponent>().Single().SetParametersAsync(ParameterView.Empty);
            rows.Sections.SelectMany(section => section.Options).ShouldContain(option => option.Name == revealed);
            rows.Sections.SelectMany(section => section.Options).ShouldNotContain(option => option.Name == hidden);
            node.Configuration[revealed].GetInt32().ShouldBe(27);
            node.Configuration[hidden].GetString().ShouldBe("keep");
            graph.Undo();
            graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration[toggle].GetString().ShouldBe(initial);
        });
    }
}
