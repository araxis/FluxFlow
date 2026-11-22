using System.Reflection;
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
    [InlineData("serial.reader", false)]
    [InlineData("serial.reader", true)]
    [InlineData("serial.writer", false)]
    [InlineData("serial.writer", true)]
    [InlineData("nats.jetstream.publisher", false)]
    [InlineData("nats.jetstream.publisher", true)]
    [InlineData("nats.jetstream.consumer", false)]
    [InlineData("nats.jetstream.consumer", true)]
    public async Task Transport_visual_dialog_conditions_preserve_hidden_values_and_save_cancel_history(string type, bool cancel)
    {
        var serial = type.StartsWith("serial.", StringComparison.Ordinal);
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var settings = serial
            ? """{"Type":"TYPE","Connection":"missing","fRaMiNg":"Raw","FrameLength":17,"DelimiterBase64":"AA==","Extra":{"Keep":42}}"""
            : """{"Type":"TYPE","Connection":"missing","Stream":"events","Consumer":"worker","cReAtEsTrEaM":"false","CreateConsumer":false,"MaxStreamBytes":9007199254740993,"AckWaitSeconds":47,"Extra":{"Keep":42}}""";
        graph.LoadJson("{\"Workflows\":{\"main\":{\"transport\":" + settings.Replace("TYPE", type) + "}}}");
        graph.ResetHistory();
        graph.AddEnvironment("Undo me"); graph.Undo();
        var before = graph.ToProjectJson();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        graph.Diagram.SelectModel(node, true);
        var registration = scope.ServiceProvider.GetServices<IComponentConfigurator>().Single(item => item.Component == type);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
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
            await SwitchStructuredMode(dialog, false);
            var reveal = serial ? "FrameLength" : "MaxStreamBytes";
            var label = serial ? "Frame bytes" : "Stream bytes";
            var inputLabel = $"aria-label=\"{label}\"";
            view.ToHtmlString().ShouldNotContain(inputLabel);
            var controller = components.Items.OfType<OptionEditorField>().Last(field => field.Option.Name == (serial ? "Framing" : "CreateStream"));
            var draft = controller.Configuration;
            await SetMode(true);
            view.ToHtmlString().ShouldContain(inputLabel);
            var hiddenInteger = components.Items.OfType<ValidatedTextField>().Last(field => field.Label == label);
            await SetMode(false);
            view.ToHtmlString().ShouldNotContain(inputLabel);
            // A removed editor must not be able to write through a late callback.
            await hiddenInteger.ValueChanged.InvokeAsync("99");
            draft[reveal].GetInt64().ShouldBe(serial ? 17 : 9007199254740993L);
            await SetMode(true);

            if (serial)
            {
                var framing = components.Items.OfType<MudSelect<string>>().Last(field => field.UserAttributes?.GetValueOrDefault("aria-label") as string == "Framing");
                await framing.ValueChanged.InvokeAsync("Delimiter");
                var delimiter = components.Items.OfType<MudTextField<string>>().Last(field => field.UserAttributes?.GetValueOrDefault("aria-label") as string == "Delimiter (Base64)");
                await delimiter.ValueChanged.InvokeAsync("not base64!");
                await SwitchStructuredMode(dialog, true);
                view.ToHtmlString().ShouldNotContain("settings-json");
                draft["DelimiterBase64"].GetString().ShouldBe("AA==");
                graph.ToProjectJson().ShouldBe(before);
                await delimiter.ValueChanged.InvokeAsync(" AA== ");
                draft["DelimiterBase64"].GetString().ShouldBe("AA==");
                await framing.ValueChanged.InvokeAsync("FixedLength");
            }

            await SwitchStructuredMode(dialog, true);
            var source = components.Items.OfType<ExpressionCodeEditor>().Last();
            var edited = source.Value;
            edited.ShouldContain(serial ? "AA==" : "AckWaitSeconds");
            await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = "{" }));
            await SwitchStructuredMode(dialog, false);
            view.ToHtmlString().ShouldContain("settings-json");
            graph.ToProjectJson().ShouldBe(before);
            await source.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = edited }));
            await SwitchStructuredMode(dialog, false);
            view.ToHtmlString().ShouldContain(inputLabel);
            graph.ToProjectJson().ShouldBe(before);
            graph.CanRedo.ShouldBeTrue();

            var buttons = components.Items.OfType<MudButton>();
            var action = cancel
                ? buttons.Single(button => button.Color != Color.Primary && button.Size != Size.Small && button.OnClick.HasDelegate)
                : buttons.Single(button => button.Variant == Variant.Filled && button.Color == Color.Primary);
            await action.OnClick.InvokeAsync(new MouseEventArgs());
            pending.IsCompleted.ShouldBeTrue();
            await pending;
            if (cancel)
            {
                graph.ToProjectJson().ShouldBe(before);
                graph.CanRedo.ShouldBeTrue();
            }
            else
            {
                var saved = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration;
                saved[reveal].GetInt64().ShouldBe(serial ? 17 : 9007199254740993L);
                saved["Extra"].GetProperty("Keep").GetInt32().ShouldBe(42);
                if (serial) saved["fRaMiNg"].GetString().ShouldBe("FixedLength");
                else saved["cReAtEsTrEaM"].GetBoolean().ShouldBeTrue();
                var after = graph.ToProjectJson();
                graph.Undo(); graph.ToProjectJson().ShouldBe(before);
                graph.Redo(); graph.ToProjectJson().ShouldBe(after);
                graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
            }

            async Task SetMode(bool enabled)
            {
                if (serial)
                    await components.Items.OfType<MudSelect<string>>().Last(field => field.UserAttributes?.GetValueOrDefault("aria-label") as string == "Framing").ValueChanged.InvokeAsync(enabled ? "FixedLength" : "Raw");
                else
                    await components.Items.OfType<MudCheckBox<bool>>().Last(field => field.UserAttributes?.GetValueOrDefault("aria-label") as string == "Create stream").ValueChanged.InvokeAsync(enabled);
            }
        });
    }

    private static async Task SwitchStructuredMode(StructuredSettingsDialog dialog, bool json)
    {
        var method = typeof(StructuredSettingsDialog).GetMethod("ChangeMode", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(dialog, [json])!;
        await dialog.SetParametersAsync(ParameterView.Empty);
    }
}
