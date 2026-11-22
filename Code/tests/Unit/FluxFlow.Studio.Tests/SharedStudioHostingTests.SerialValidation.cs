using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Plugins;
using FluxFlow.Studio.UI.Features.Observations.Validation;
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
    [InlineData("serial.reader")]
    [InlineData("serial.writer")]
    public async Task Serial_cross_field_diagnostics_follow_repairs_modes_and_history_without_rewriting_values(string type)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        LoadFrameGraph(graph, type);
        graph.Diagram.Links.Count.ShouldBe(1);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentConfiguratorComponent.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var editor = components.Items.OfType<PortablePropertyConfigurator>().Single();
            diagnostics.Messages.Values.Select(message => message.Path).Order().ShouldBe(["Frame bytes", "Read buffer"]);
            graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            await Field("Read buffer").ValueChanged.InvokeAsync("16");
            await Rebind();
            diagnostics.Messages.Values.Single().Path.ShouldBe("Frame bytes");
            var framing = components.Items.OfType<MudSelect<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Framing"));
            await framing.ValueChanged.InvokeAsync("Raw");
            await Rebind();
            diagnostics.Messages.ShouldBeEmpty();
            graph.SelectedNode!.Configuration["FrameLength"].GetInt32().ShouldBe(64);
            graph.SelectedNode.Configuration["DelimiterBase64"].GetString().ShouldBe(" AA== ");
            var after = graph.ToProjectJson();
            graph.Undo(); await Rebind();
            diagnostics.Messages.Values.Single().Path.ShouldBe("Frame bytes");
            graph.Undo(); await Rebind();
            diagnostics.Messages.Values.Select(message => message.Path).Order().ShouldBe(["Frame bytes", "Read buffer"]);
            graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo(); await Rebind();
            diagnostics.Messages.ShouldBeEmpty(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
            graph.Diagram.Links.Count.ShouldBe(1);
            editor.Dispose();

            MudTextField<string> Field(string name) => components.Items.OfType<MudTextField<string>>()
                .Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), name));
            Task Rebind() => editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(ComponentConfiguratorComponent.Configuration)] = graph.SelectedNode!.Configuration }));
        });
    }

    [Theory]
    [InlineData("serial.reader", false)]
    [InlineData("serial.reader", true)]
    [InlineData("serial.writer", false)]
    [InlineData("serial.writer", true)]
    public async Task Serial_dialog_reports_cross_field_errors_and_blocks_save_until_repaired(string type, bool cancel)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        LoadFrameGraph(graph, type);
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "serial"), true);
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
            { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            await renderer.RenderComponentAsync<PortablePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentConfiguratorComponent.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var pending = components.Items.OfType<PortablePropertyConfigurator>().Single().OpenEditorAsync();
            var dialog = components.Items.OfType<StructuredSettingsDialog>().Single();
            await SwitchStructuredMode(dialog, false);
            view.ToHtmlString().ShouldContain("Read buffer must not exceed");
            var save = components.Items.OfType<MudButton>().Single(button => button.Variant == Variant.Filled && button.Color == Color.Primary);
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            pending.IsCompleted.ShouldBeFalse(); graph.ToProjectJson().ShouldBe(before);
            view.ToHtmlString().ShouldContain("mud-alert");
            await components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Max frame bytes"))
                .ValueChanged.InvokeAsync("64");
            view.ToHtmlString().ShouldNotContain("must not exceed");
            view.ToHtmlString().ShouldNotContain("mud-alert");
            await SwitchStructuredMode(dialog, true);
            await SwitchStructuredMode(dialog, false);
            if (cancel)
                await components.Items.OfType<MudButton>().Single(button => button.Color != Color.Primary && button.Size != Size.Small && button.OnClick.HasDelegate)
                    .OnClick.InvokeAsync(new MouseEventArgs());
            else await save.OnClick.InvokeAsync(new MouseEventArgs());
            pending.IsCompleted.ShouldBeTrue(); await pending;
            if (cancel) { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); }
            else
            {
                var saved = graph.SelectedNode!.Configuration;
                saved["mAxFrAmEbYtEs"].GetInt32().ShouldBe(64);
                saved["ReadBufferBytes"].GetInt32().ShouldBe(32);
                saved["FrameLength"].GetInt32().ShouldBe(64);
                saved["DelimiterBase64"].GetString().ShouldBe(" AA== ");
                saved["Extra"].GetProperty("Keep").GetBoolean().ShouldBeTrue();
                var after = graph.ToProjectJson();
                graph.Undo(); graph.ToProjectJson().ShouldBe(before);
                graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            }
        });
    }

    private static void LoadFrameGraph(DesignerGraphState graph, string type)
    {
        graph.LoadJson("""
            {"Workflows":{"main":{
              "serial":{"Type":"TYPE","Connection":"missing","Framing":"FixedLength","mAxFrAmEbYtEs":16,"ReadBufferBytes":32,"FrameLength":64,"DelimiterBase64":" AA== ","Extra":{"Keep":true},"Output":"sink.Input"},
              "sink":{"Type":"logger"}
            }}}
            """.Replace("TYPE", type));
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "serial"), true);
    }
}
