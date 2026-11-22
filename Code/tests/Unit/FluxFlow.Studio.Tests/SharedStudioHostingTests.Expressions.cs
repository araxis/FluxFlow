using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Evaluation;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    [Theory]
    [InlineData("selection")]
    [InlineData("revision")]
    [InlineData("dispose")]
    [InlineData("value")]
    [InlineData("engine")]
    [InlineData("condition")]
    [InlineData("preview")]
    [InlineData("previewProvider")]
    [InlineData("previewSettings")]
    [InlineData("previewSample")]
    [InlineData("resultKind")]
    [InlineData("cancel")]
    [InlineData("unchanged")]
    [InlineData("save")]
    public async Task Expression_dialog_preserves_target_and_history(string action)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"first":{"Type":"mapper","Engine":"jsonata","Expression":"$.Reading","Extra":42},"second":{"Type":"mapper","Engine":"jsonata","Expression":"$.Reading"}}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(n => n.NodeName == "first");
        graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        ExpressionField? field = null;
        var changes = 0;
        var openings = 0;
        string? atClose = null;
        bool redoAtClose = false;
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += async reference =>
        {
            openings++;
            if (openings == 1) await field!.Edit(); // Re-entrant clicks must not open a second dialog.
            if (action == "selection")
            {
                graph.Diagram.UnselectAll();
                graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(n => n.NodeName == "second"), true);
            }
            else if (action == "revision") { graph.AddEnvironment("Other change"); graph.Undo(); }
            else if (action == "dispose") field!.Dispose();
            else if (action is "value" or "engine" or "condition" or "preview" or "resultKind" or "previewProvider" or "previewSettings" or "previewSample")
            {
                var parameter = action switch { "value" => nameof(ExpressionField.Value), "engine" => nameof(ExpressionField.Engine), "preview" => nameof(ExpressionField.PreviewEnabled), "resultKind" => nameof(ExpressionField.ResultKind), "previewProvider" => nameof(ExpressionField.PreviewProvider), "previewSettings" => nameof(ExpressionField.PreviewSettings), "previewSample" => nameof(ExpressionField.PreviewSample), _ => nameof(ExpressionField.IsCondition) };
                object replacement = action switch { "value" => "$.Other", "engine" => "other", "preview" => false, "resultKind" => ExpressionResultKind.String, "previewProvider" => "other", "previewSettings" => new Dictionary<string, JsonElement> { ["Locale"] = JsonSerializer.SerializeToElement("sv") }, "previewSample" => "{\"state\":0,\"input\":1}", _ => true };
                await field!.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [parameter] = replacement }));
            }
            atClose = graph.ToProjectJson(); redoAtClose = graph.CanRedo;
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(action == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(action == "unchanged" ? "$.Reading" : "  $.Reading * 2  "));
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<ExpressionField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ExpressionField.Value)] = "$.Reading",
                [nameof(ExpressionField.Engine)] = "jsonata",
                [nameof(ExpressionField.ValueChanged)] = EventCallback.Factory.Create<string>(this, value =>
                {
                    changes++; node.Configuration["Expression"] = JsonSerializer.SerializeToElement(value); graph.RecordEdit();
                })
            }));
            field = components.Items.OfType<ExpressionField>().Single();
            await field.Edit();
        });
        openings.ShouldBe(1);
        changes.ShouldBe(action == "save" ? 1 : 0);
        if (action == "save")
        {
            node.Configuration["Expression"].GetString().ShouldBe("  $.Reading * 2  ");
            node.Configuration["Extra"].GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        }
        else
        {
            graph.ToProjectJson().ShouldBe(atClose); graph.CanRedo.ShouldBe(redoAtClose);
        }
    }

    [Theory]
    [InlineData("changeFilter", "KeySelector", ExpressionResultKind.String)]
    [InlineData("deduplicate", "KeySelector", ExpressionResultKind.String)]
    [InlineData("retry", "ShouldRetry", ExpressionResultKind.Boolean)]
    public async Task Typed_component_expression_fields_use_the_shared_preview_without_mutation(string type, string property, ExpressionResultKind kind)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var option = scope.ServiceProvider.GetServices<IComponentDescription>().Single(d => d.Palette.ComponentType == type)
            .Inspector.Sections.SelectMany(s => s.Options).Single(o => o.Name == property);
        var configuration = new Dictionary<string, JsonElement>
        {
            ["eNgInE"] = JsonSerializer.SerializeToElement("jsonata"),
            [property] = JsonSerializer.SerializeToElement("file:original.jsonata")
        };
        var before = JsonSerializer.Serialize(configuration);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(OptionEditorField.Option)] = option, [nameof(OptionEditorField.Configuration)] = configuration }));
            var field = components.Items.OfType<ExpressionField>().Single();
            field.ResultKind.ShouldBe(kind); field.IsCondition.ShouldBeFalse(); field.PreviewEnabled.ShouldBeTrue();
            field.PreviewProvider.ShouldBeNull(); field.PreviewSettings.ShouldBeEmpty();
            field.Engine.ShouldBe("jsonata"); field.Value.ShouldBe("file:original.jsonata");
        });
        JsonSerializer.Serialize(configuration).ShouldBe(before);
    }

    [Theory]
    [InlineData("mapper", "Expression", "file:sample.jsonata", "save")]
    [InlineData("mapper", "Expression", "$input", "save", "file:map reading.jsonata")]
    [InlineData("mapper", "Expression", "file:sample.jsonata", "cancel")]
    [InlineData("mapper", "Expression", "  InLiNe: [$input]  ", "unchanged")]
    [InlineData("dataGenerator", "Expression", "file:sample.jsonata", "save")]
    [InlineData("dataGenerator", "Expression", "base64:JGlucHV0", "cancel")]
    [InlineData("dataGenerator", "Expression", "  $random()  ", "unchanged")]
    [InlineData("reducer", "Reduce", "file:reduce.jsonata", "save")]
    [InlineData("reducer", "Reduce", "  $state + $input  ", "cancel")]
    [InlineData("reducer", "Reduce", "base64:JHN0YXRl", "unchanged")]
    [InlineData("reducer", "AreEqual", "$previous = $current", "save")]
    [InlineData("reducer", "AreEqual", "file:equal.jsonata", "cancel")]
    [InlineData("reducer", "AreEqual", "  $previous = $current  ", "unchanged")]
    [InlineData("changeFilter", "KeySelector", "file:keys.jsonata", "save")]
    [InlineData("changeFilter", "KeySelector", "base64:JGlucHV0", "cancel")]
    [InlineData("changeFilter", "KeySelector", "  $string(id)  ", "unchanged")]
    [InlineData("deduplicate", "KeySelector", "file:keys.jsonata", "save")]
    [InlineData("deduplicate", "KeySelector", "base64:JGlucHV0", "cancel")]
    [InlineData("deduplicate", "KeySelector", "  $string(id)  ", "unchanged")]
    public async Task Component_source_fields_share_editing_without_rewriting_saved_configuration(string type, string property, string source, string action, string replacement = "  $input  ")
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"item":{"Type":"TYPE","Extra":42}}}}""".Replace("TYPE", type));
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var key = property.ToUpperInvariant();
        node.Configuration[key] = JsonSerializer.SerializeToElement(source);
        if (type != "dataGenerator")
            node.Configuration["eNgInE"] = JsonSerializer.SerializeToElement("jsonata");
        else node.Configuration["lOcAlE"] = JsonSerializer.SerializeToElement("sv");
        if (type == "reducer")
        {
            node.Configuration["InitialState"] = JsonSerializer.SerializeToElement("file:initial.json");
        }
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var model = scope.ServiceProvider.GetServices<IComponentDescription>().Single(d => d.Palette.ComponentType == type).Inspector;
        var option = model.Sections.SelectMany(s => s.Options).Single(o => o.Name == property);
        scope.ServiceProvider.GetRequiredService<IDialogService>().DialogInstanceAddedAsync += reference =>
        {
            reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
            reference.Dismiss(action == "cancel" ? DialogResult.Cancel() : DialogResult.Ok(action == "unchanged" ? source : replacement));
            return Task.CompletedTask;
        };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var expression = components.Items.OfType<ExpressionField>().Single();
            expression.Engine.ShouldBe("jsonata"); expression.PreviewEnabled.ShouldBeTrue(); expression.Value.ShouldBe(source);
            if (type == "reducer")
            {
                expression.PreviewProvider.ShouldBe(option.PreviewProvider);
                expression.PreviewSample.ShouldBe(option.PreviewSample);
                expression.ResultKind.ShouldBe(property == "AreEqual" ? ExpressionResultKind.Boolean : ExpressionResultKind.Value);
                expression.PreviewSettings.ShouldHaveSingleItem().Key.ShouldBe("eNgInE");
            }
            if (type == "dataGenerator")
            {
                expression.PreviewProvider.ShouldBe("FluxFlow.Plugins.Sources.dataGenerator");
                expression.PreviewSettings.ShouldHaveSingleItem().Key.ShouldBe("lOcAlE");
                expression.PreviewSettings["lOcAlE"].GetString().ShouldBe("sv");
            }
            graph.ToProjectJson().ShouldBe(before);
            await components.Items.OfType<OptionEditorField>().Single().OpenEditorAsync();
        });
        node.Configuration[key].GetString().ShouldBe(action == "save" ? replacement : source);
        node.Configuration.ContainsKey(property).ShouldBeFalse();
        if (type == "dataGenerator") node.Configuration.ContainsKey("Engine").ShouldBeFalse();
        else if (type == "reducer") node.Configuration["InitialState"].GetString().ShouldBe("file:initial.json");
        if (action == "save")
        {
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        }
        else { graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue(); }
    }

    [Fact]
    public async Task Source_expression_dialog_does_not_create_a_mapper_preview()
    {
        var services = CreateServices(); var components = new CapturingComponents(); var evaluator = new UnexpectedExpressionEvaluation();
        services.AddSingleton<IComponentActivator>(components); services.AddSingleton<IExpressionEvaluator>(evaluator);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialog = await dialogs.ShowAsync<ExpressionEditorDialog>("Reduce", new DialogParameters<ExpressionEditorDialog>
            { { d => d.Value, "file:reduce.jsonata" }, { d => d.Engine, "jsonata" }, { d => d.PreviewEnabled, false } });
            var editor = components.Items.OfType<ExpressionCodeEditor>().Single();
            editor.Value.ShouldBe("file:reduce.jsonata");
            await editor.Ready.InvokeAsync();
            components.Items.OfType<SampleMetadataEditor>().ShouldBeEmpty();
            evaluator.Calls.ShouldBe(0);
            dialog.Close(DialogResult.Cancel());
            (await dialog.Result)!.Canceled.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Custom_preview_uses_sample_json_without_message_controls_and_reset_preserves_that_layout()
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IExpressionEvaluator, UnexpectedExpressionEvaluation>();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
        dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var rendered = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialog = await dialogs.ShowAsync<ExpressionEditorDialog>("Reduce", new DialogParameters<ExpressionEditorDialog>
            { { d => d.Value, "$state + $input" }, { d => d.Engine, "jsonata" }, { d => d.PreviewSample, "{\"state\":4,\"input\":3}" } });
            var input = components.Items.OfType<ExpressionCodeEditor>().Single(e => e.Label == "Sample input");
            input.Value.ShouldBe("{\"state\":4,\"input\":3}");
            components.Items.OfType<SampleMetadataEditor>().ShouldBeEmpty();
            rendered.ToHtmlString().ShouldNotContain("Sample message type");
            var workspace = components.Items.OfType<FluxFlow.Studio.UI.Features.Designer.Components.Workspace.DockWorkspace>().Single();
            workspace.Layout.Contains("Message").ShouldBeFalse();
            workspace.Layout.Reset(); workspace.Layout.Contains("Message").ShouldBeFalse();
            dialog.Close(DialogResult.Cancel());
            (await dialog.Result)!.Canceled.ShouldBeTrue();
        });
    }

    private sealed class UnexpectedExpressionEvaluation : IExpressionEvaluator
    {
        public int Calls { get; private set; }
        public Task<ExpressionEvaluationResult> EvaluateAsync(ExpressionEvaluationRequest request, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Source editing must not evaluate a mapper sample."); }
    }
}
