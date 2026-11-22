using System.Globalization;
using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.JetStream;
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
    [InlineData("nats.jetstream.publisher", "MaxStreamMessages", "9007199254740993")]
    [InlineData("nats.jetstream.publisher", "MaxStreamBytes", "9223372036854775807")]
    [InlineData("nats.jetstream.consumer", "MaxStreamMessages", "9223372036854775807")]
    [InlineData("nats.jetstream.consumer", "MaxStreamBytes", "9007199254740993")]
    [InlineData("nats.jetstream.consumer", "MaxStreamBytes", "\"09007199254740993\"")]
    public async Task JetStream_integer_fields_preserve_exact_values_representation_and_history(string type, string property, string saved)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var key = property.ToLowerInvariant();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"transport\":{\"Type\":\"" + type + "\",\"CreateStream\":true,\"" + key + "\":" + saved + ",\"Extra\":{\"Keep\":42}}}}}");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var option = JetStreamDescription.Options(type).Single(option => option.Name == property);
        option.Editor.ShouldBe(OptionEditorKind.Integer);
        var registration = scope.ServiceProvider.GetServices<IComponentConfigurator>().Single(item => item.Component == type);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<DynamicComponent>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DynamicComponent.Type)] = registration.ComponentType,
                [nameof(DynamicComponent.Parameters)] = new Dictionary<string, object>
                {
                    [nameof(ComponentConfiguratorComponent.Configuration)] = node.Configuration,
                    [nameof(ComponentConfiguratorComponent.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
                }
            }));
            var field = components.Items.OfType<ValidatedTextField>().Single(field => field.Label == option.DisplayName);
            var input = components.Items.OfType<MudTextField<string>>().Single(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), option.DisplayName));
            var original = saved.Trim('"');
            field.Value.ShouldBe(original);
            input.Value.ShouldBe(original);
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            foreach (var invalid in new[] { "1.5", "0", "-1", "9223372036854775808", "NaN", "1e3" })
            {
                field.Validate(invalid).ShouldNotBeNull();
                await input.ValueChanged.InvokeAsync(invalid);
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            }
            var number = long.Parse(original, CultureInfo.InvariantCulture);
            await input.ValueChanged.InvokeAsync("+" + number.ToString(CultureInfo.InvariantCulture));
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            var changed = number - 1;
            await input.ValueChanged.InvokeAsync(changed.ToString(CultureInfo.InvariantCulture));
            node.Configuration[key].GetInt64().ShouldBe(changed);
            node.Configuration.ContainsKey(property).ShouldBeFalse();
            node.Configuration["Extra"].GetProperty("Keep").GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
            graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration[key].GetInt64().ShouldBe(changed);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"invalid\"")]
    public async Task Integer_field_preserves_omitted_or_invalid_saved_values_until_an_explicit_edit(string? saved)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var configuration = new Dictionary<string, JsonElement>();
        if (saved is not null) configuration["Count"] = JsonDocument.Parse(saved).RootElement.Clone();
        var original = JsonSerializer.Serialize(configuration);
        var commits = 0;
        var option = OptionEditorModel.Create("Count", "Count", OptionEditorKind.Integer, defaultValue: 10L) with { Min = 1, Max = 20 };
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.Configuration)] = configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, () => commits++)
            }));
            var field = components.Items.OfType<ValidatedTextField>().Single();
            JsonSerializer.Serialize(configuration).ShouldBe(original); commits.ShouldBe(0);
            if (saved is "null" or "\"\"")
            {
                await field.ValueChanged.InvokeAsync(null);
                JsonSerializer.Serialize(configuration).ShouldBe(original); commits.ShouldBe(0);
            }
            field.Validate("21").ShouldNotBeNull();
            await field.ValueChanged.InvokeAsync("21");
            JsonSerializer.Serialize(configuration).ShouldBe(original);
            if (saved is null)
            {
                await field.ValueChanged.InvokeAsync("10");
                configuration.ShouldBeEmpty(); commits.ShouldBe(0);
            }
            await field.ValueChanged.InvokeAsync("17"); configuration["Count"].GetInt64().ShouldBe(17); commits.ShouldBe(1);
            await field.ValueChanged.InvokeAsync(null); configuration.ShouldBeEmpty(); commits.ShouldBe(2);
            await field.ValueChanged.InvokeAsync(null); commits.ShouldBe(2);
            components.Items.OfType<OptionEditorField>().Single().Dispose();
            await field.ValueChanged.InvokeAsync("12"); configuration.ShouldBeEmpty(); commits.ShouldBe(2);
        });
    }
}
