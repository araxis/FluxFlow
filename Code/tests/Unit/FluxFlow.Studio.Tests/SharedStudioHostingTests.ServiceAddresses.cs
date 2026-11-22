using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Environments.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Theory]
    [InlineData("nats.connection", "Endpoint", "nats://broker.example:4222", "nats://broker.example:4222")]
    [InlineData("http.client", "BaseAddress", "https://user:sample@host.example/path?token=example#part", "https://host.example/path (query hidden)")]
    [InlineData("serial.port", "PortName", "/dev/ttyUSB0", "serial-port://local/%2Fdev%2FttyUSB0")]
    [InlineData("ble.peripheral", "Endpoint", "ble://112233445566/random", "ble://112233445566/random")]
    [InlineData("ble.adapter", "Endpoint", "ble-adapter://default", "ble-adapter://default")]
    public async Task Existing_service_shows_its_own_address_even_when_another_resource_is_malformed(
        string type, string property, string value, string expected)
    {
        await using var provider = CreateServices().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"other":{"Type":"http.client","BaseAddress":[]}},"Workflows":{"main":{}}}""");
        graph.SetConnection("service", null, new JsonObject { ["Type"] = type, [property] = value }, add: true);
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudBlazor.MudPopoverProvider>();
            var editor = await renderer.RenderComponentAsync<ServiceSettingsEditor>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ServiceSettingsEditor.ResourcePath)] = "Resources:service" }));
            var html = editor.ToHtmlString();
            html.ShouldContain(expected);
            html.ShouldNotContain("user:sample");
            html.ShouldNotContain("token=example");
        });
        graph.ToProjectJson().ShouldBe(original);
        graph.CanUndo.ShouldBeFalse();
    }
}
