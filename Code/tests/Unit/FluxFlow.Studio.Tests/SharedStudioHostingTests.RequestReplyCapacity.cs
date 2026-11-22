using System.Text;
using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.RequestReply;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Observations.Validation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Theory]
    [InlineData("Maximum in flight", "1")]
    [InlineData("Buffer capacity", "0")]
    public async Task Request_reply_capacity_repair_preserves_saved_values_and_history(string field, string repair)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"reply":{"Type":"requestReply","maxinflight":"2147483647","buffercapacity":"00128","Extra":42}}}}""");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        var before = graph.ToProjectJson();
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ComponentInspector>(Parameters());
            var inspector = components.Items.OfType<ComponentInspector>().Single();
            diagnostics.Messages.Count.ShouldBe(2);
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            // Both fields can repair an invalid saved combination. Clearing the buffer
            // would restore its default 128, so it must not bypass the combined limit.
            await Field("Buffer capacity").ValueChanged.InvokeAsync("");
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await Field(field).ValueChanged.InvokeAsync(repair);
            await inspector.SetParametersAsync(Parameters());
            diagnostics.Messages.ShouldBeEmpty();
            var saved = graph.SelectedNode!.Configuration;
            saved["Extra"].GetInt32().ShouldBe(42);
            saved[field == "Maximum in flight" ? "buffercapacity" : "maxinflight"].GetString()
                .ShouldBe(field == "Maximum in flight" ? "00128" : "2147483647");
            var after = graph.ToProjectJson();
            graph.Undo(); await inspector.SetParametersAsync(Parameters());
            graph.ToProjectJson().ShouldBe(before); diagnostics.Messages.Count.ShouldBe(2);
            graph.Redo(); await inspector.SetParametersAsync(Parameters());
            graph.ToProjectJson().ShouldBe(after); diagnostics.Messages.ShouldBeEmpty();
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);

            MudTextField<string> Field(string name) => components.Items.OfType<MudTextField<string>>()
                .Last(input => Equals(input.UserAttributes?.GetValueOrDefault("aria-label"), name));
        });

        ParameterView Parameters()
        {
            var node = graph.SelectedNode!;
            return ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ComponentInspector.Configuration)] = node.Configuration,
                [nameof(ComponentInspector.Model)] = node.Description.GetInspector(node.Configuration),
                [nameof(ComponentInspector.NodeKey)] = "main.reply",
                [nameof(ComponentInspector.NodeName)] = node.NodeName,
                [nameof(ComponentInspector.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            });
        }
    }

    [Theory]
    [InlineData("{}", "MaxInFlight", "2147483519", true)]
    [InlineData("{}", "MaxInFlight", "2147483520", false)]
    [InlineData("{}", "BufferCapacity", "2147483646", true)]
    [InlineData("{}", "BufferCapacity", "2147483647", false)]
    [InlineData("{\"BufferCapacity\":0}", "MaxInFlight", "2147483647", true)]
    [InlineData("{\"maxinflight\":\"2147483647\",\"BufferCapacity\":0}", "BufferCapacity", "", false)]
    [InlineData("{\"BufferCapacity\":null}", "MaxInFlight", "2147483520", false)]
    [InlineData("{\"BufferCapacity\":0}", "MaxInFlight", "", true)]
    public async Task Request_reply_editor_capacity_bounds_agree_with_native_registration(string json, string property, string draft, bool accepted)
    {
        var services = CreateServices(); services.AddRequestReply();
        var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var description = scope.ServiceProvider.GetServices<IComponentDescription>().Single(item => item.Palette.ComponentType == "requestReply");
        var option = description.GetInspector(values).Sections.SelectMany(section => section.Options).Single(item => item.Name == property);
        var original = JsonSerializer.Serialize(values);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option, [nameof(OptionEditorField.Configuration)] = values
            }));
            var field = components.Items.OfType<ValidatedTextField>().Single();
            (field.Validate(draft) is null).ShouldBe(accepted);
            await components.Items.OfType<MudTextField<string>>().Single().ValueChanged.InvokeAsync(draft);
            if (!accepted) JsonSerializer.Serialize(values).ShouldBe(original);
        });
        var candidate = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(original)!;
        if (draft.Length == 0) candidate.Remove(property);
        else candidate[property] = JsonSerializer.SerializeToElement(draft);
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Component = candidate })))).Build();
        var check = provider.GetRequiredKeyedService<ComponentConfigurationCheck>("requestReply");
        var error = Record.Exception(() => check(configuration, configuration.GetSection("Component")));
        if (accepted) error.ShouldBeNull(); else error.ShouldBeOfType<ArgumentOutOfRangeException>();
    }
}
