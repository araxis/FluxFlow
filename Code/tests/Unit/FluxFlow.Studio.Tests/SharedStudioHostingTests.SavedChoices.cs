using System.Text.Json;
using FluxFlow.Authoring.Definitions.JetStream;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    [InlineData("Storage", "\"file\"", "Memory")]
    [InlineData("Storage", "0", "Memory")]
    [InlineData("Storage", "\"future-storage\"", "File")]
    [InlineData("DeliverPolicy", "\"new\"", "All")]
    [InlineData("DeliverPolicy", "\"future-policy\"", "New")]
    public async Task Shared_choices_display_unlisted_saved_values_without_normalizing_configuration(string property, string saved, string replacement)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var key = property.ToLowerInvariant();
        graph.LoadJson("""{"Workflows":{"main":{"transport":{"Type":"nats.jetstream.consumer","PROPERTY":SAVED,"Extra":42}}}}"""
            .Replace("PROPERTY", key).Replace("SAVED", saved));
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo(); graph.Diagram.SelectModel(graph.Diagram.Nodes.Single(), true);
        var before = graph.ToProjectJson();
        var option = JetStreamDescription.Options("nats.jetstream.consumer").Single(option => option.Name == property);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var select = components.Items.OfType<MudSelect<string>>().Single();
            var text = JsonSerializer.Deserialize<JsonElement>(saved).ToString();
            select.GetState(field => field.Value).ShouldBe(text);
            var choices = components.Items.OfType<MudSelectItem<string>>().ToArray();
            choices.Single(choice => choice.Value == text).Disabled.ShouldBeTrue();
            choices.Where(choice => !choice.Disabled).Select(choice => choice.Value).ShouldBe(option.Choices.Select(choice => choice.Value));
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await select.ValueChanged.InvokeAsync(text);
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await select.ValueChanged.InvokeAsync(replacement);
            graph.SelectedNode!.Configuration[key].GetString().ShouldBe(replacement);
            graph.SelectedNode.Configuration["Extra"].GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }
}
