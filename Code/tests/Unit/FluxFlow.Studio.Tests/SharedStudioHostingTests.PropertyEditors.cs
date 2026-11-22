using System.Text.Json;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    [InlineData("revision")]
    [InlineData("dispose")]
    [InlineData("replace")]
    public async Task Shared_property_dialog_rejects_results_after_its_target_changes(string change)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"router":{"Type":"router","Routes":{"Original":"true"},"Custom":{"Keep":42}}}}}""");
        graph.ResetHistory();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var original = node.Configuration["Routes"].GetRawText();
        OptionEditorField? field = null;
        var changes = 0;
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += async reference =>
        {
            if (change == "revision") graph.AddEnvironment("Changed");
            else if (change == "dispose") ((IDisposable)field!).Dispose();
            else await field!.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Configuration)] = new Dictionary<string, JsonElement>()
            }));
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(DialogResult.Ok("""{"Replacement":"false"}"""));
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = OptionEditorModel.Create("Routes", "Routes", OptionEditorKind.Json),
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, () => changes++)
            }));
            field = components.Items.OfType<OptionEditorField>().Single();
            await field.OpenEditorAsync();
            changes.ShouldBe(0);
            node.Configuration["Routes"].GetRawText().ShouldBe(original);
            if (change != "revision") graph.CanUndo.ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData("{ \"Original\" : \"true\" }")]
    [InlineData("{\n\"Original\":\"true\"\n}")]
    public async Task Shared_property_dialog_preserves_original_JSON_and_redo_for_formatting_only_changes(string edited)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"router":{"Type":"router","Routes":{"Original":"true"},"Custom":{"Keep":42}}}}}""");
        graph.ResetHistory();
        graph.AddEnvironment("Undo me"); graph.Undo();
        graph.CanRedo.ShouldBeTrue();
        var before = graph.ToProjectJson();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var original = node.Configuration["Routes"].GetRawText();
        var changes = 0;
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(DialogResult.Ok(edited));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = OptionEditorModel.Create("Routes", "Routes", OptionEditorKind.Json),
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, () => { changes++; graph.RecordEdit(); })
            }));
            await components.Items.OfType<OptionEditorField>().Single().OpenEditorAsync();
            changes.ShouldBe(0);
            node.Configuration["Routes"].GetRawText().ShouldBe(original);
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
        });
    }
}
