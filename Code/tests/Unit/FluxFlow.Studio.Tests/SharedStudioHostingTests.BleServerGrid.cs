using FluxFlow.Studio.UI.Shared.Configuration;
using FluxFlow.Plugins.Bluetooth.Design;
using System.Text.Json;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Plugins;
using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
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
    private const string ServerGridConfiguration = """
        {"Type":"ble.server","aDaPtEr":"Resources.missing","IsConnectable":false,"oUtPuTcApAcItY":7,
         "ServiceId":"3f63b38e-98df-4a52-a9f8-76b803a934b2",
         "Characteristics":[{"Id":"f50d7529-4e5d-4088-b88f-e236cd1e7f29","Value":"original","Extra":42}],"Future":{"Keep":true}}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BLE_server_grid_preserves_hierarchy_missing_adapter_and_history(bool multiple)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var draft = BleServerDraft.FromJson(ServerGridConfiguration);
        if (multiple) draft.AddService(FluxFlow.Components.Bluetooth.BleStandards.BatteryTemplate(false));
        graph.LoadJson("{\"Workflows\":{\"main\":{\"server\":" + draft.ToJson() + "}}}");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var hierarchy = node.Configuration[multiple ? "Services" : "Characteristics"].GetRawText();
        var commits = 0;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration,
                [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, () => { commits++; graph.RecordEdit(); })
            }));
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            view.ToHtmlString().ShouldContain("GATT services");
            var connection = components.Items.OfType<ConnectionSelect>().Single();
            connection.ResourceType.ShouldBe("ble.adapter"); connection.Value.ShouldBe("Resources.missing");
            var capacity = components.Items.OfType<ValidatedTextField>().Single(field => field.Label == "Output capacity");
            capacity.Validate("0").ShouldNotBeNull(); capacity.Validate("1.5").ShouldNotBeNull();
            await capacity.ValueChanged.InvokeAsync("7"); graph.CanRedo.ShouldBeTrue();
            await capacity.ValueChanged.InvokeAsync("19");
            await Task.Yield();
            commits.ShouldBe(1);
            var afterCapacity = graph.ToProjectJson();
            node = graph.SelectedNode!;
            await components.Items.OfType<PortablePropertyConfigurator>().Single().SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                { [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration }));
            await components.Items.OfType<MudCheckBox<bool>>().Last(field => Equals(field.UserAttributes?["aria-label"], "Connectable")).ValueChanged.InvokeAsync(true);
            await Task.Yield();
            commits.ShouldBe(2);
            node.Configuration["oUtPuTcApAcItY"].GetInt32().ShouldBe(19);
            node.Configuration["aDaPtEr"].GetString().ShouldBe("Resources.missing");
            node.Configuration[multiple ? "Services" : "Characteristics"].GetRawText().ShouldBe(hierarchy);
            node.Configuration["Future"].GetProperty("Keep").GetBoolean().ShouldBeTrue();
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(afterCapacity);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Theory]
    [InlineData("save")]
    [InlineData("cancel")]
    [InlineData("no-op")]
    [InlineData("stale")]
    [InlineData("disposed")]
    public async Task BLE_server_dialog_commits_only_current_confirmed_changes(string action)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"server\":" + ServerGridConfiguration + "}}}");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var original = JsonSerializer.Serialize(node.Configuration); var before = graph.ToProjectJson();
        var dialogCount = 0;
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            dialogCount++;
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            var value = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(original)!;
            if (action != "no-op") value["oUtPuTcApAcItY"] = JsonSerializer.SerializeToElement(21);
            // Object ordering alone must not consume Redo or create an edit.
            value = value.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
            if (action == "stale") graph.AddEnvironment("Changed");
            if (action == "disposed") components.Items.OfType<PortablePropertyConfigurator>().Single().Dispose();
            reference.Dismiss(action == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(JsonSerializer.Serialize(value)));
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
            await components.Items.OfType<PortablePropertyConfigurator>().Single().OpenEditorAsync();
            dialogCount.ShouldBe(1);
            if (action == "save")
            {
                node.Configuration["oUtPuTcApAcItY"].GetInt32().ShouldBe(21);
                var after = graph.ToProjectJson(); graph.Undo(); graph.ToProjectJson().ShouldBe(before);
                graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            }
            else
            {
                JsonSerializer.Serialize(node.Configuration).ShouldBe(original);
                if (action != "stale") { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); }
            }
        });
    }

    [Fact]
    public async Task BLE_server_advanced_JSON_starts_with_original_settings_and_cancel_is_detached()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await dialogs.ShowAsync<StructuredSettingsDialog>("BLE GATT server", new DialogParameters<StructuredSettingsDialog>
                { { field => field.Value, ServerGridConfiguration }, { field => field.Hierarchy, BleGattDocument.Editor }, { field => field.StartWithJson, true } });
            components.Items.OfType<ExpressionCodeEditor>().Single().Value.ShouldBe(ServerGridConfiguration);
            await components.Items.OfType<MudButton>().Single(button => button.Color != Color.Primary).OnClick.InvokeAsync(new MouseEventArgs());
            (await dialog.Result)!.Canceled.ShouldBeTrue();
        });
    }
}
