using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    [InlineData(OptionEditorKind.Integer, "10", "1.5", "17", false)]
    [InlineData(OptionEditorKind.Integer, "10", "9223372036854775808", "9223372036854775807", false)]
    [InlineData(OptionEditorKind.Duration, "\"00:00:10\"", "invalid", "00:00:17", false)]
    [InlineData(OptionEditorKind.DateTimeOffset, "\"2030-01-01T00:00:00+01:00\"", "invalid", "2030-02-01T00:00:00+01:00", false)]
    [InlineData(OptionEditorKind.Integer, "10", "1.5", "17", true)]
    [InlineData(OptionEditorKind.Duration, "\"00:00:10\"", "invalid", "00:00:17", true)]
    [InlineData(OptionEditorKind.DateTimeOffset, "\"2030-01-01T00:00:00+01:00\"", "invalid", "2030-02-01T00:00:00+01:00", true)]
    public async Task Structured_dialog_retains_invalid_visual_drafts_until_corrected_or_cancelled(
        OptionEditorKind kind, string saved, string invalid, string corrected, bool cancel)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{}}}"""); graph.ResetHistory();
        graph.AddEnvironment("Undo me"); graph.Undo(); var before = graph.ToProjectJson();
        graph.CanRedo.ShouldBeTrue();
        var validated = 0;
        var original = "{\"Type\":\"sample\",\"Value\":" + saved + ",\"Other\":\"original\",\"Extra\":{\"Keep\":42}}";
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var reference = await dialogs.ShowAsync<StructuredSettingsDialog>("Settings", new DialogParameters<StructuredSettingsDialog>
            {
                { dialog => dialog.Type, "sample" }, { dialog => dialog.Value, original },
                { dialog => dialog.Options, new[] { OptionEditorModel.Create("Value", "Value", kind), OptionEditorModel.Create("Other", "Other") } },
                { dialog => dialog.Validate, (Action<System.Text.Json.Nodes.JsonObject>)(_ => validated++) }
            });
            var dialog = components.Items.OfType<StructuredSettingsDialog>().Single();
            var input = components.Items.OfType<MudTextField<string>>().Single(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Value"));
            var other = components.Items.OfType<MudTextField<string>>().Single(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Other"));
            await other.ValueChanged.InvokeAsync("draft change");
            await input.ValueChanged.InvokeAsync(invalid);
            var save = components.Items.OfType<MudButton>().Single(button => button.Variant == Variant.Filled);
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            reference.Result!.IsCompleted.ShouldBeFalse(); validated.ShouldBe(0);
            await SwitchStructuredMode(dialog, true);
            view.ToHtmlString().ShouldNotContain("settings-json");
            input.Value.ShouldBe(invalid);
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            if (cancel)
            {
                await components.Items.OfType<MudButton>().Single(button => button.Variant != Variant.Filled && button.OnClick.HasDelegate).OnClick.InvokeAsync(new MouseEventArgs());
                (await reference.Result)!.Canceled.ShouldBeTrue(); validated.ShouldBe(0);
            }
            else
            {
                await input.ValueChanged.InvokeAsync(corrected);
                await SwitchStructuredMode(dialog, true);
                view.ToHtmlString().ShouldContain("settings-json");
                await SwitchStructuredMode(dialog, false);
                await save.OnClick.InvokeAsync(new MouseEventArgs());
                var result = (await reference.Result)!;
                result.Canceled.ShouldBeFalse(); validated.ShouldBe(1);
                using var json = JsonDocument.Parse(result.Data.ShouldBeOfType<string>());
                json.RootElement.GetProperty("Value").ToString().ShouldBe(corrected);
                json.RootElement.GetProperty("Other").GetString().ShouldBe("draft change");
                json.RootElement.GetProperty("Extra").GetProperty("Keep").GetInt32().ShouldBe(42);
            }
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            dialog.Value.ShouldBe(original);
        });
    }

    [Fact]
    public async Task Structured_dialog_does_not_keep_validation_errors_from_removed_conditional_fields()
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var reference = await dialogs.ShowAsync<StructuredSettingsDialog>("Settings", new DialogParameters<StructuredSettingsDialog>
            {
                { dialog => dialog.Type, "sample" },
                { dialog => dialog.Value, """{"Type":"sample","Enabled":true,"Count":17,"Extra":42}""" },
                { dialog => dialog.Options, new[] { OptionEditorModel.Create("Enabled", "Enabled", OptionEditorKind.Toggle), OptionEditorModel.Create("Count", "Count", OptionEditorKind.Integer) } },
                { dialog => dialog.IsVisible, (Func<OptionEditorModel, IDictionary<string, JsonElement>, bool>)((option, draft) => option.Name != "Count" || draft["Enabled"].GetBoolean()) }
            });
            var input = components.Items.OfType<MudTextField<string>>().Single();
            await input.ValueChanged.InvokeAsync("invalid");
            await components.Items.OfType<MudCheckBox<bool>>().Single().ValueChanged.InvokeAsync(false);
            view.ToHtmlString().ShouldNotContain("aria-label=\"Count\"");
            await components.Items.OfType<MudButton>().Single(button => button.Variant == Variant.Filled).OnClick.InvokeAsync(new MouseEventArgs());
            reference.Result!.IsCompleted.ShouldBeTrue();
            using var json = JsonDocument.Parse((await reference.Result)!.Data.ShouldBeOfType<string>());
            json.RootElement.GetProperty("Count").GetInt32().ShouldBe(17);
            json.RootElement.GetProperty("Enabled").GetBoolean().ShouldBeFalse();
            json.RootElement.GetProperty("Extra").GetInt32().ShouldBe(42);
        });
    }
}
