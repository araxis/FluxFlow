using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Studio.UI.Features.Connections;
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
    [InlineData("router", "Engine", "jsonata")]
    [InlineData("nats.subscriber", "Subject", "sensors.*")]
    public async Task Required_text_fields_retain_invalid_drafts_and_exact_saved_values(string type, string property, string? saved)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var option = scope.ServiceProvider.GetServices<IComponentDescription>().Single(item => item.Palette.ComponentType == type)
            .Inspector.Sections.SelectMany(section => section.Options).Single(item => item.Name == property);
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
                    [nameof(OptionEditorField.Option)] = option,
                    [nameof(OptionEditorField.Configuration)] = node.Configuration,
                    [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
                }));
                diagnostics.Messages.Count.ShouldBe(string.IsNullOrWhiteSpace(saved) ? 1 : 0);
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
                var input = components.Items.OfType<MudTextField<string>>().Single();
                foreach (var invalid in new[] { "", " ", "\t" })
                {
                    await input.ValueChanged.InvokeAsync(invalid);
                    graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
                    diagnostics.Messages.Count.ShouldBe(1);
                }
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    await input.ValueChanged.InvokeAsync(saved);
                    graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
                    diagnostics.Messages.ShouldBeEmpty();
                }
                await input.ValueChanged.InvokeAsync("exact-reference");
                diagnostics.Messages.ShouldBeEmpty();
                graph.SelectedNode!.Configuration[saved is null ? property : key].GetString().ShouldBe("exact-reference");
            });
        }
        diagnostics.Messages.ShouldBeEmpty();
        graph.SelectedNode!.Configuration["Extra"].GetInt32().ShouldBe(42);
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Theory]
    [MemberData(nameof(ComponentConnectionCases))]
    public async Task Component_connection_field_filters_connections_and_preserves_missing_references_until_selected(string type, string property, string resourceType, string saved)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        var serial = resourceType == "serial.port";
        var fixture = serial
            ? """{"Resources":{"global":{"Type":"serial.port"}},"Workflows":{"main":{"Resources":{"api":{"Type":"serial.port","PortName":"COM3"},"broker":{"Type":"mqtt.client"}},"request":{"Type":"COMPONENT","Extra":42}},"other":{"Resources":{"api":{"Type":"serial.port","PortName":"COM4"}}}}}"""
            : """{"Resources":{"api":{"Type":"RESOURCE","BaseAddress":"https://example.test","Endpoint":"nats://example.test:4222"},"broker":{"Type":"mqtt.client","Host":"localhost"}},"Workflows":{"main":{"Resources":{"local":{"Type":"RESOURCE"}},"request":{"Type":"COMPONENT","Extra":42}},"other":{"Resources":{"api":{"Type":"RESOURCE"}}}}}""";
        graph.LoadJson(fixture.Replace("RESOURCE", resourceType).Replace("COMPONENT", type));
        var address = serial ? "main.Resources.api" : "Resources.api";
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var key = property.ToLowerInvariant();
        node.Configuration[key] = JsonSerializer.SerializeToElement(saved);
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var option = node.Description.Inspector.Sections.SelectMany(section => section.Options).Single(item => item.Name == property);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.Configuration)] = node.Configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var selector = components.Items.OfType<ConnectionSelect>().Single();
            selector.ResourceType.ShouldBe(resourceType); selector.AllowEmpty.ShouldBeFalse();
            var items = components.Items.OfType<MudSelectItem<string>>().ToArray();
            var expected = serial ? new[] { saved == "api" ? "api" : address }
                : type == "rateLimit" || resourceType == "ros.node" ? new[] { saved == "api" ? "api" : address, "main.Resources.local" }
                : new[] { saved == "api" ? "api" : address, "main.Resources.local", "other.Resources.api" };
            items.Where(item => !item.Disabled).Select(item => item.Value).ShouldBe(expected);
            if (saved != "api" && saved != "main.Resources.local") items.Single(item => item.Disabled).Value.ShouldBe(saved);
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await selector.ValueChanged.InvokeAsync(saved);
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await selector.ValueChanged.InvokeAsync(address);
        });
        graph.SelectedNode!.Configuration[key].GetString().ShouldBe(address);
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    public static IEnumerable<object[]> ComponentConnectionCases()
    {
        foreach (var saved in new[] { "api", "main.Resources.local", "Resources.removed", "Resources.broker", " Host:limiter ", "other.Resources.api" })
            yield return ["rateLimit", "Limiter", "rateLimiter.tokenBucket", saved];
        foreach (var type in new[] { "http.client", "nats.publisher", "nats.subscriber", "nats.request", "nats.reply", "nats.jetstream.publisher", "nats.jetstream.consumer" })
        foreach (var saved in new[] { "api", "Resources.removed", "Resources.broker" })
            yield return [type, type == "http.client" ? "Client" : "Connection", type == "http.client" ? "http.client" : "nats.connection", saved];
        foreach (var type in new[] { "serial.text", "serial.reader", "serial.writer" })
        foreach (var saved in new[] { "api", "other.Resources.api", "Resources.global", "broker", "removed" })
            yield return [type, "Connection", "serial.port", saved];
        foreach (var type in new[] { "ros.publisher", "ros.subscriber" })
        foreach (var saved in new[] { "api", "Resources.removed", "Resources.broker", "other.Resources.api" })
            yield return [type, "Node", "ros.node", saved];
    }
}
