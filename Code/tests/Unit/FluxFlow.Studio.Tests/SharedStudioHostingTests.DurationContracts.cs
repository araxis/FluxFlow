using System.Globalization;
using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    [InlineData("batch", "MaxWait", null)]
    [InlineData("batch", "MaxWait", "00:00:00")]
    [InlineData("batch", "MaxWait", "00:00:02.0000000")]
    [InlineData("requestReply", "ReplyTimeout", null)]
    [InlineData("requestReply", "ReplyTimeout", "00:00:00")]
    [InlineData("requestReply", "ReplyTimeout", "00:00:02.0000000")]
    [InlineData("rateLimit", "ResponseTimeout", null)]
    [InlineData("rateLimit", "ResponseTimeout", "00:00:00")]
    [InlineData("rateLimit", "ResponseTimeout", "00:00:02.0000000")]
    [InlineData("retry", "AttemptTimeout", null)]
    [InlineData("retry", "AttemptTimeout", "00:00:00")]
    [InlineData("retry", "AttemptTimeout", "00:00:02.0000000")]
    public async Task Duration_contract_bounds_preserve_invalid_saved_values_and_history_until_explicit_repair(string type, string property, string? saved)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var option = scope.ServiceProvider.GetServices<IComponentDescription>().Single(item => item.Palette.ComponentType == type)
            .Inspector.Sections.SelectMany(section => section.Options).Single(item => item.Name == property);
        option.MinDuration.ShouldBe(TimeSpan.FromTicks(1));
        option.MaxDuration.ShouldBe(TimeSpan.FromMilliseconds(uint.MaxValue - 1));
        option.AllowInfiniteDuration.ShouldBe(type is "rateLimit" or "retry");
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"item\":{\"Type\":\"" + type + "\",\"Extra\":42}}}}");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var key = property.ToLowerInvariant();
        if (saved is not null) node.Configuration[key] = JsonSerializer.SerializeToElement(saved);
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
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
                diagnostics.Messages.Count.ShouldBe(saved == "00:00:00" || saved is null && option.IsRequired ? 1 : 0);
                var field = components.Items.OfType<ValidatedTextField>().Single();
                var input = components.Items.OfType<MudTextField<string>>().Single();
                var invalid = new List<string> { "not a duration", "00:00:00", "-00:00:00.002", (option.MaxDuration!.Value + TimeSpan.FromTicks(1)).ToString("c", CultureInfo.InvariantCulture) };
                if (!option.AllowInfiniteDuration) invalid.Add("-00:00:00.001");
                if (option.IsRequired) invalid.Add("");
                foreach (var text in invalid)
                {
                    field.Validate(text).ShouldNotBeNull();
                    await input.ValueChanged.InvokeAsync(text);
                    graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
                }
                field.Validate(TimeSpan.FromTicks(1).ToString("c", CultureInfo.InvariantCulture)).ShouldBeNull();
                field.Validate(option.MaxDuration.Value.ToString("c", CultureInfo.InvariantCulture)).ShouldBeNull();
                if (saved == "00:00:02.0000000")
                {
                    await input.ValueChanged.InvokeAsync(saved);
                    graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
                }
                if (option.AllowInfiniteDuration) field.Validate("-00:00:00.0010000").ShouldBeNull();
                var valid = type == "rateLimit" && saved is not null ? "-00:00:00.0010000" : "00:00:05.2500000";
                await input.ValueChanged.InvokeAsync(valid);
                diagnostics.Messages.ShouldBeEmpty();
                graph.SelectedNode!.Configuration[saved is null ? property : key].GetString().ShouldBe(valid);
            });
        }
        diagnostics.Messages.ShouldBeEmpty();
        graph.SelectedNode!.Configuration["Extra"].GetInt32().ShouldBe(42);
        var after = graph.ToProjectJson(); after.ShouldNotBe(before);
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public async Task Gate_initial_state_editor_preserves_omitted_default_and_history()
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var option = scope.ServiceProvider.GetServices<IComponentDescription>().Single(item => item.Palette.ComponentType == "gate")
            .Inspector.Sections.SelectMany(section => section.Options).Single(item => item.Name == "InitiallyOpen");
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"gate":{"Type":"gate","Extra":42}}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var input = components.Items.OfType<MudCheckBox<bool>>().Single();
            await input.ValueChanged.InvokeAsync(false);
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await input.ValueChanged.InvokeAsync(true);
            graph.SelectedNode!.Configuration["InitiallyOpen"].GetBoolean().ShouldBeTrue();
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }
}
