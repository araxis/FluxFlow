using System.Text.Json;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
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
    [MemberData(nameof(NatsEditorCases))]
    public async Task Nats_whole_component_editor_validates_drafts_and_preserves_cancel_no_op_and_history(string type, string action)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var settings = """{"Type":"TYPE","Connection":"Resources.broker","Subject":"events.>","QueueGroup":"workers","Stream":"events","Consumer":"worker","CreateStream":true,"Storage":"file","MaxStreamMessages":"9223372036854775807","CreateConsumer":true,"DeliverPolicy":"new","cApAcItY":"00128","Extra":{"Bytes":"AP8=","Keep":42}}""".Replace("TYPE", type);
        graph.LoadJson("""{"Resources":{"broker":{"Type":"nats.connection","Endpoint":"nats://example.test:4222"}},"Workflows":{"main":{"worker":{"Type":"nats.jetstream.consumer"},"transport":SETTINGS}}}""".Replace("SETTINGS", settings));
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "transport");
        graph.Diagram.SelectModel(node, true); var before = graph.ToProjectJson();
        var registration = scope.ServiceProvider.GetServices<IComponentConfigurator>().Single(item => item.Component == type);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<MudDialogProvider>();
            scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
            { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            await renderer.RenderComponentAsync<DynamicComponent>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DynamicComponent.Type)] = registration.ComponentType,
                [nameof(DynamicComponent.Parameters)] = new Dictionary<string, object>
                {
                    [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration,
                    [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
                }
            }));
            var pending = components.Items.OfType<ComponentConfiguratorComponent>().Single().OpenEditorAsync();
            var dialog = components.Items.OfType<StructuredSettingsDialog>().Single();
            var source = components.Items.OfType<ExpressionCodeEditor>().Last();
            var original = source.Value;
            var save = components.Items.OfType<MudButton>().Single(button => button.Variant == Variant.Filled && button.Color == Color.Primary);
            foreach (var invalid in new[] { "{", original.Replace("\"00128\"", "0") })
            {
                await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = invalid }));
                await save.OnClick.InvokeAsync(new MouseEventArgs());
                pending.IsCompleted.ShouldBeFalse(); graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            }
            await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = original }));
            await SwitchStructuredMode(dialog, false);
            var input = components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Buffer size"));
            await input.ValueChanged.InvokeAsync("0");
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            pending.IsCompleted.ShouldBeFalse(); graph.ToProjectJson().ShouldBe(before);
            await input.ValueChanged.InvokeAsync(action == "no-op" ? "128" : "256");
            await SwitchStructuredMode(dialog, true);
            var json = components.Items.OfType<ExpressionCodeEditor>().Last().Value;
            using (var parsed = JsonDocument.Parse(json))
            {
                parsed.RootElement.GetProperty("Storage").GetString().ShouldBe("file");
                parsed.RootElement.GetProperty("DeliverPolicy").GetString().ShouldBe("new");
                parsed.RootElement.GetProperty("MaxStreamMessages").GetString().ShouldBe("9223372036854775807");
                parsed.RootElement.GetProperty("Extra").GetProperty("Bytes").GetString().ShouldBe("AP8=");
            }
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            var button = action == "cancel"
                ? components.Items.OfType<MudButton>().Single(button => button.Color != Color.Primary && button.Size != Size.Small && button.OnClick.HasDelegate)
                : save;
            await button.OnClick.InvokeAsync(new MouseEventArgs());
            pending.IsCompleted.ShouldBeTrue(); await pending;
            if (action != "save")
            {
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            }
            else
            {
                graph.SelectedNode!.Configuration["cApAcItY"].GetInt32().ShouldBe(256);
                var after = graph.ToProjectJson();
                graph.Undo(); graph.ToProjectJson().ShouldBe(before);
                graph.Redo(); graph.ToProjectJson().ShouldBe(after);
                graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
            }
        });
    }

    public static IEnumerable<object[]> NatsEditorCases()
    {
        foreach (var type in new[] { "nats.publisher", "nats.subscriber", "nats.request", "nats.reply", "nats.jetstream.publisher", "nats.jetstream.consumer", "nats.jetstream.settlement" })
        foreach (var action in new[] { "save", "cancel", "no-op" })
            yield return [type, action];
    }
}
