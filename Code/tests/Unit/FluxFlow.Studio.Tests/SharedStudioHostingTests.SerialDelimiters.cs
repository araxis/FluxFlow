using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.Serial;
using FluxFlow.Authoring.Definitions.SerialText;
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
    [InlineData("serial.text", null)]
    [InlineData("serial.text", "\r\n")]
    [InlineData("serial.reader", null)]
    [InlineData("serial.reader", " Cg==\r\n")]
    [InlineData("serial.writer", "AP+A")]
    public async Task Serial_delimiter_editors_preserve_saved_values_defaults_and_history(string type, string? saved)
    {
        var text = type == "serial.text";
        var option = text ? new SerialTextDescription().Inspector.Sections.Single().Options.Single(item => item.Name == "NewLine")
            : SerialFrameDescription.Options.Single(item => item.Name == "DelimiterBase64");
        var key = option.Name.ToLowerInvariant();
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var settings = new Dictionary<string, object> { ["Type"] = type, ["Extra"] = new { Keep = 42 } };
        if (saved is not null) settings[key] = saved;
        graph.LoadJson("{\"Workflows\":{\"main\":{\"serial\":" + JsonSerializer.Serialize(settings) + "}}}");
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var select = components.Items.OfType<MudSelect<string>>().Single();
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            if (text)
            {
                option.Choices.Select(item => item.DisplayName).ShouldBe(new[] { "LF", "CRLF" });
                await select.ValueChanged.InvokeAsync(saved ?? "\n");
            }
            else
            {
                await select.ValueChanged.InvokeAsync("");
                var field = components.Items.OfType<ValidatedTextField>().Last();
                var input = components.Items.OfType<MudTextField<string>>().Last();
                field.Value.ShouldBe(saved ?? "Cg==");
                foreach (var invalid in new[] { "", "!", "A", Convert.ToBase64String(new byte[65]) })
                {
                    field.Validate(invalid).ShouldNotBeNull();
                    await input.ValueChanged.InvokeAsync(invalid);
                    graph.ToProjectJson().ShouldBe(before);
                }
                field.Validate(Convert.ToBase64String(new byte[64])).ShouldBeNull();
                await input.ValueChanged.InvokeAsync(" " + (saved ?? "Cg==") + " ");
            }
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            var changed = text ? (saved == "\r\n" ? "\n" : "\r\n") : "DQo=";
            await select.ValueChanged.InvokeAsync(changed);
            var property = saved is null ? option.Name : key;
            node.Configuration[property].GetString().ShouldBe(changed);
            node.Configuration["Extra"].GetProperty("Keep").GetInt32().ShouldBe(42);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
            graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration[property].GetString().ShouldBe(changed);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid!")]
    [InlineData("")]
    public async Task Serial_binary_delimiter_preserves_invalid_saved_values_and_rejects_late_callbacks(string? saved)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var configuration = new Dictionary<string, JsonElement> { ["DelimiterBase64"] = JsonSerializer.SerializeToElement(saved) };
        var before = JsonSerializer.Serialize(configuration); var changes = 0;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = SerialFrameDescription.Options.Single(item => item.Name == "DelimiterBase64"),
                [nameof(OptionEditorField.Configuration)] = configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, () => changes++)
            }));
            var field = components.Items.OfType<ValidatedTextField>().Single();
            field.Validate(field.Value).ShouldNotBeNull();
            JsonSerializer.Serialize(configuration).ShouldBe(before); changes.ShouldBe(0);
            var input = components.Items.OfType<MudTextField<string>>().Single();
            await input.ValueChanged.InvokeAsync("AP+A");
            configuration["DelimiterBase64"].GetString().ShouldBe("AP+A"); changes.ShouldBe(1);
            var owner = components.Items.OfType<Base64Field>().Single(); owner.Dispose();
            await input.ValueChanged.InvokeAsync("AA==");
            await components.Items.OfType<MudSelect<string>>().Single().ValueChanged.InvokeAsync("Cg==");
            configuration["DelimiterBase64"].GetString().ShouldBe("AP+A"); changes.ShouldBe(1);
        });
    }
}
