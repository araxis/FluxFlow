using System.Globalization;
using System.Text.Json;
using FluxFlow.Authoring.Definitions.Deduplicate;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Pages;
using FluxFlow.Studio.UI.Features.Observations.Validation;
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
    [InlineData(null)]
    [InlineData("00:00:00")]
    [InlineData("-00:00:00.001")]
    [InlineData("invalid saved duration")]
    [InlineData("100.00:00:00.0000001")]
    public async Task Deduplicate_retention_requires_a_positive_value_without_imposing_a_timer_limit(string? saved)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"filter":{"Type":"deduplicate","Engine":"custom-runner","KeySelector":"file:keys.expr","Extra":42}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        if (saved is not null) node.Configuration["rEtEnTiOn"] = JsonSerializer.SerializeToElement(saved);
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var option = new DeduplicateDescription().Inspector.Sections.SelectMany(section => section.Options).Single(item => item.Name == "Retention");
        option.MinDuration.ShouldBe(TimeSpan.FromTicks(1)); option.MaxDuration.ShouldBeNull();
        option.AllowInfiniteDuration.ShouldBeFalse();
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        await using (var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>()))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(OptionEditorField.Configuration)] = node.Configuration,
                    [nameof(OptionEditorField.Option)] = option,
                    [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
                }));
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
                diagnostics.Messages.Count.ShouldBe(saved == "100.00:00:00.0000001" ? 0 : 1);
                var field = components.Items.OfType<ValidatedTextField>().Single();
                var input = components.Items.OfType<MudTextField<string>>().Single();
                foreach (var invalid in new[] { "", " ", "not a duration", "00:00:00", "-00:00:00.001" })
                {
                    field.Validate(invalid).ShouldNotBeNull();
                    await input.ValueChanged.InvokeAsync(invalid);
                    graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
                }
                field.Validate(TimeSpan.MaxValue.ToString("c", CultureInfo.InvariantCulture)).ShouldBeNull();
                await input.ValueChanged.InvokeAsync("00:00:00.0000001");
                diagnostics.Messages.ShouldBeEmpty();
            });
        }
        graph.SelectedNode!.Configuration[saved is null ? "Retention" : "rEtEnTiOn"].GetString().ShouldBe("00:00:00.0000001");
        graph.SelectedNode.Configuration["Engine"].GetString().ShouldBe("custom-runner");
        graph.SelectedNode.Configuration["KeySelector"].GetString().ShouldBe("file:keys.expr");
        graph.SelectedNode.Configuration["Extra"].GetInt32().ShouldBe(42);
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Theory]
    [InlineData("changeFilter")]
    [InlineData("deduplicate")]
    public async Task Filtering_inspector_preserves_engine_comparison_capacity_and_exact_source_through_history(string type)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"filter\":{\"Type\":\"" + type + "\",\"eNgInE\":\"custom-runner\",\"KeySelector\":\"file:keys.expr\",\"iGnOrEcAsE\":\"true\",\"Retention\":\"100.00:00:00.0000001\",\"MaxKeys\":2048,\"OutputCapacity\":256,\"Extra\":42}}}}");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<DesignerPage>();
            MudTextField<string> Text(string label) => components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), label));
            MudCheckBox<bool> Comparison() => components.Items.OfType<MudCheckBox<bool>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Ignore case"));
            await Comparison().ValueChanged.InvokeAsync(true);
            await Text("Engine").ValueChanged.InvokeAsync(" ");
            await Text("Output capacity").ValueChanged.InvokeAsync("0");
            if (type == "deduplicate") await Text("Maximum keys").ValueChanged.InvokeAsync("0");
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await Text("Engine").ValueChanged.InvokeAsync("jsonata");
            var engineOnly = graph.ToProjectJson();
            await Comparison().ValueChanged.InvokeAsync(false);
            var comparisonOnly = graph.ToProjectJson();
            await Text("Output capacity").ValueChanged.InvokeAsync("512");
            var capacityOnly = graph.ToProjectJson();
            if (type == "deduplicate") await Text("Maximum keys").ValueChanged.InvokeAsync("4096");
            var after = graph.ToProjectJson();
            graph.SelectedNode!.Configuration["eNgInE"].GetString().ShouldBe("jsonata");
            graph.SelectedNode.Configuration["iGnOrEcAsE"].GetBoolean().ShouldBeFalse();
            graph.SelectedNode.Configuration["KeySelector"].GetString().ShouldBe("file:keys.expr");
            graph.SelectedNode.Configuration["Retention"].GetString().ShouldBe("100.00:00:00.0000001");
            graph.SelectedNode.Configuration["Extra"].GetInt32().ShouldBe(42);
            if (type == "deduplicate") { graph.Undo(); graph.ToProjectJson().ShouldBe(capacityOnly); }
            graph.Undo(); graph.ToProjectJson().ShouldBe(comparisonOnly);
            graph.Undo(); graph.ToProjectJson().ShouldBe(engineOnly);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo(); graph.Redo();
            if (type == "deduplicate") graph.Redo();
            graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }
}
