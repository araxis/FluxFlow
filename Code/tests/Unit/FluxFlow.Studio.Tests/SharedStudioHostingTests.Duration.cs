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
using MudBlazor.Extensions;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Theory]
    [InlineData("\"00:00:02\"", "00:00:05.2500000")]
    [InlineData("\"invalid saved duration\"", "1.00:00:00")]
    [InlineData("null", "-00:00:00.001")]
    [InlineData(null, "00:00:09")]
    [InlineData("\"00:00:02\"", "")]
    public async Task Duration_editor_keeps_invalid_drafts_out_of_configuration_and_preserves_history(string? saved, string valid)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"timer":{"Type":"scheduler","Extra":42}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        if (saved is not null) node.Configuration["iNtErVaL"] = JsonSerializer.Deserialize<JsonElement>(saved);
        graph.ResetHistory();
        if (saved != "null") { graph.AddEnvironment("Undo me"); graph.Undo(); }
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var canRedo = graph.CanRedo;
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        await using (var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>()))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(OptionEditorField.Configuration)] = node.Configuration,
                    [nameof(OptionEditorField.Option)] = OptionEditorModel.Create("Interval", "Interval", OptionEditorKind.Duration, defaultValue: "00:00:01"),
                    [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
                }));
                graph.ToProjectJson().ShouldBe(before);
                var input = components.Items.OfType<MudTextField<string>>().Single();
                input.GetState(field => field.Value).ShouldBe(saved is null ? "00:00:01" : JsonSerializer.Deserialize<JsonElement>(saved).ToString());
                diagnostics.Messages.Count.ShouldBe(saved?.Contains("invalid") == true ? 1 : 0);
                await input.ValueChanged.InvokeAsync("not a duration");
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBe(canRedo);
                diagnostics.Messages.Values.Single().Path.ShouldBe("Interval");
                await components.Items.OfType<MudIconButton>().Single(button => button.UserAttributes?.GetValueOrDefault("aria-label") as string == "Show validation for Interval")
                    .OnClick.InvokeAsync(new MouseEventArgs());
                diagnostics.Selected.ShouldNotBeNull();
                await input.ValueChanged.InvokeAsync(valid);
                diagnostics.Messages.ShouldBeEmpty();
            });
        }
        diagnostics.Messages.ShouldBeEmpty();
        var key = saved is null ? "Interval" : "iNtErVaL";
        if (valid.Length == 0) node.Configuration.ContainsKey(key).ShouldBeFalse();
        else node.Configuration[key].GetString().ShouldBe(valid);
        node.Configuration["Extra"].GetInt32().ShouldBe(42);
        var after = graph.ToProjectJson(); after.ShouldNotBe(before);
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }
}
