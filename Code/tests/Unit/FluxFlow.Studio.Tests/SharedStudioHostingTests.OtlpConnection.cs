using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Studio.UI.Features.Connections;

using FluxFlow.Studio.UI.Shared.Configuration;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
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
    [InlineData("HttpProtobuf", "00:00:00.001", true)]
    [InlineData("Grpc", "00:00:10", true)]
    [InlineData("Grpc", "24.20:31:23.647", true)]
    [InlineData("Grpc", "00:00:00.0009999", false)]
    [InlineData("HttpProtobuf", "24.20:31:23.648", false)]
    public void OTLP_description_covers_native_settings_and_timeout_bounds(string protocol, string timeout, bool valid)
    {
        using var provider = CreateServices().BuildServiceProvider();
        var description = provider.GetServices<ConnectionDescription>().Single(item => item.Type == "logging.otlp");
        var settings = description.Create();
        var options = description.PropertyGroups!(settings).SelectMany(group => group.Options).ToArray();
        options.Select(item => item.Name).Concat(description.PropertyGroups!(settings).SelectMany(group => group.StringMaps).Select(map => map.Name))
            .Order().ShouldBe(typeof(OtlpLoggerConfiguration).GetProperties().Select(item => item.Name).Order());
        options.Single(item => item.Name == "Endpoint").ValidateText!("ftp://invalid.test").ShouldNotBeNull();
        var duration = options.Single(item => item.Name == "ExportTimeout");
        var parsed = TimeSpan.Parse(timeout, System.Globalization.CultureInfo.InvariantCulture);
        (parsed >= duration.MinDuration && parsed <= duration.MaxDuration).ShouldBe(valid);
        settings["Protocol"] = protocol; settings["ExportTimeout"] = timeout;
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(
            new JsonObject { ["Resource"] = settings }.ToJsonString()))).Build();
        using var native = new ServiceCollection().AddLogger().BuildServiceProvider();
        var registration = native.GetRequiredKeyedService<ResourceRegistration>("logging.otlp");
        Action register = () => registration(new ServiceCollection(), ApplicationAddress.Resource("collector"), configuration.GetSection("Resource"));
        if (valid) Should.NotThrow(register); else Should.Throw<ArgumentOutOfRangeException>(register);
        // Registration validates settings but does not construct or contact an exporter.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OTLP_visual_settings_preserve_headers_unknown_fields_and_history(bool numericEnums)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"collector":{"Type":"logging.otlp","endpoint":"https://collector.test/v1/logs","hEaDeRs":"Authorization=Bearer original==,X-Tenant=saved","Protocol":"HttpProtobuf","MinimumLogLevel":"Trace","Future":42}},"Workflows":{"main":{"log":{"Type":"logger","Logger":"Resources.collector"}}}}""");
        if (numericEnums)
        {
            var settings = graph.Connections.Single().Settings.DeepClone().AsObject();
            settings["Protocol"] = 1; settings["MinimumLogLevel"] = 0;
            graph.SetConnection("collector", null, settings);
        }
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:collector" }));
            MudTextField<string> Field(string label) => components.Items.OfType<MudTextField<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), label));
            MudSelect<string> Choice(string label) => components.Items.OfType<MudSelect<string>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), label));
            components.Items.OfType<StringMapPropertyGrid>().Single().SecretValues.ShouldBeTrue();
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await Field("Timeout").ValueChanged.InvokeAsync("00:00:00"); await Task.Yield();
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await Field("Service name").ValueChanged.InvokeAsync("test-service"); await Task.Yield();
            graph.Connections.Single().Settings["Protocol"]!.ToJsonString().ShouldBe(numericEnums ? "1" : "\"HttpProtobuf\"");
            await Field("Endpoint").ValueChanged.InvokeAsync("https://collector.test:4317/"); await Task.Yield();
            await Choice("Protocol").ValueChanged.InvokeAsync("Grpc"); await Task.Yield();
            await Field("Timeout").ValueChanged.InvokeAsync("00:00:05.2500000"); await Task.Yield();
            await Choice("Minimum level").ValueChanged.InvokeAsync("Warning"); await Task.Yield();
            await components.Items.OfType<MudCheckBox<bool>>().Last(field => Equals(field.UserAttributes?.GetValueOrDefault("aria-label"), "Report failures")).ValueChanged.InvokeAsync(true); await Task.Yield();
            var saved = graph.Connections.Single().Settings;
            saved["hEaDeRs"]!.GetValue<string>().ShouldBe("Authorization=Bearer original==,X-Tenant=saved");
            saved["Future"]!.GetValue<int>().ShouldBe(42);
            saved["endpoint"]!.GetValue<string>().ShouldBe("https://collector.test:4317/");
            saved.ContainsKey("Endpoint").ShouldBeFalse();
            saved["ExportTimeout"]!.GetValue<string>().ShouldBe("00:00:05.2500000");
            var after = graph.ToProjectJson();
            graph.Undo(); await Task.Yield();
            graph.Connections.Single().Settings.ContainsKey("ReportExportErrors").ShouldBeFalse();
            graph.Redo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Resources.missing")]
    public async Task Logger_resource_selector_reuses_OTLP_connections_and_preserves_optional_or_missing_references(string? saved)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"collector":{"Type":"logging.otlp","Endpoint":"https://collector.test/v1/logs"},"api":{"Type":"http.client","BaseAddress":"https://example.test"}},"Workflows":{"main":{"log":{"Type":"logger"}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        if (saved is not null) node.Configuration["Logger"] = JsonSerializer.SerializeToElement(saved);
        graph.ResetHistory(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        var option = node.Description.Inspector.Sections.SelectMany(section => section.Options).Single(item => item.Name == "Logger");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Option"] = option, ["Configuration"] = node.Configuration,
                ["OnChanged"] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var selector = components.Items.OfType<ConnectionSelect>().Single();
            selector.AllowEmpty.ShouldBeTrue(); selector.ResourceType.ShouldBe("logging.otlp");
            var items = components.Items.OfType<MudSelectItem<string>>().ToArray();
            items.Where(item => !item.Disabled && !string.IsNullOrEmpty(item.Value)).Select(item => item.Value).ShouldBe(new[] { "Resources.collector" });
            graph.ToProjectJson().ShouldBe(before);
            await selector.ValueChanged.InvokeAsync("Resources.collector");
        });
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }
}
