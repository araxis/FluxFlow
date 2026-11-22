using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
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
    [InlineData("ble://AABBCCDDEEFF/public")]
    [InlineData("BLE://aabbccddeeff/random/")]
    [InlineData("ble://AA-BB-CC-DD-EE-FF/public")]
    public async Task BLE_peripheral_visual_fields_preserve_endpoints_unknown_data_and_history(string endpoint)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(new JsonObject { ["Resources"] = new JsonObject { ["device"] = new JsonObject {
            ["Type"] = "ble.peripheral", ["eNdPoInT"] = endpoint, ["Future"] = new JsonObject { ["Keep"] = 42 } } },
            ["Workflows"] = new JsonObject { ["main"] = new JsonObject() } }.ToJsonString());
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo(); var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:device" }));
            ValidatedTextField Address() => components.Items.OfType<ValidatedTextField>().Last(field => field.Label == "Device address");
            JsonObject Saved() => graph.Connections.Single().Settings;
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            var initial = Address();
            initial.Value.ShouldBe(new BleConnection(endpoint).Apply(new()).DeviceAddress);
            initial.Validate("00:00:00:00:00:00").ShouldNotBeNull();
            initial.Validate("unknown").ShouldNotBeNull();
            // Equivalent formatting must not rewrite an untouched saved endpoint.
            await initial.ValueChanged.InvokeAsync("AA:BB:CC:DD:EE:FF");
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            await initial.ValueChanged.InvokeAsync("01:02:03:04:05:06"); await Task.Yield();
            var target = new BleConnection(Saved()["eNdPoInT"]!.GetValue<string>()).Apply(new());
            target.Address.ShouldBe(0x010203040506UL);
            target.AddressType.ShouldBe(new BleConnection(endpoint).Apply(new()).AddressType);
            Saved()["Future"]!["Keep"]!.GetValue<int>().ShouldBe(42);
            Saved().ContainsKey("Endpoint").ShouldBeFalse();
            var afterAddress = graph.ToProjectJson();
            graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(afterAddress);
            await initial.ValueChanged.InvokeAsync("111213141516"); graph.ToProjectJson().ShouldBe(afterAddress);
            var choice = components.Items.OfType<MudSelect<string>>().Last();
            var changedType = target.AddressType == BleAddressType.Public ? BleAddressType.Random : BleAddressType.Public;
            await choice.ValueChanged.InvokeAsync(changedType.ToString()); await Task.Yield();
            var after = graph.ToProjectJson();
            new BleConnection(Saved()["eNdPoInT"]!.GetValue<string>()).Apply(new()).AddressType.ShouldBe(changedType);
            graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(afterAddress);
            graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
        });
    }

    [Theory]
    [InlineData("\"ble://invalid/public\"", true)]
    [InlineData("\"ble://AABBCCDDEEFF/private\"", true)]
    [InlineData("null", true)]
    [InlineData("42", false)]
    [InlineData("{\"Custom\":true}", false)]
    public async Task BLE_peripheral_invalid_saved_endpoints_remain_available_for_explicit_repair(string saved, bool text)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var config = new Dictionary<string, JsonElement> { ["Type"] = JsonSerializer.SerializeToElement("ble.peripheral"), ["Endpoint"] = JsonSerializer.Deserialize<JsonElement>(saved) };
        var before = JsonSerializer.Serialize(config); var commits = 0;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ProjectedPropertyRows>(ParameterView.FromDictionary(new Dictionary<string, object?> {
                ["Fields"] = scope.ServiceProvider.GetServices<ConnectionDescription>().Single(c => c.Type == "ble.peripheral")
                    .PropertyGroups!(new JsonObject()).Single().Fields!, ["Configuration"] = config, ["OnChanged"] = EventCallback.Factory.Create(this, () => commits++) }));
            JsonSerializer.Serialize(config).ShouldBe(before); commits.ShouldBe(0);
            if (!text) { view.ToHtmlString().ShouldContain("Advanced JSON"); return; }
            var field = components.Items.OfType<ValidatedTextField>().Single();
            field.Label.ShouldBe("Endpoint");
            field.Value.ShouldBe(JsonSerializer.Deserialize<JsonElement>(saved).ValueKind == JsonValueKind.String
                ? JsonSerializer.Deserialize<string>(saved) : saved);
            field.Validate("mqtt://localhost").ShouldNotBeNull();
            field.Validate("ble://AABBCCDDEEFF/random").ShouldBeNull();
            await field.ValueChanged.InvokeAsync("ble://AABBCCDDEEFF/random");
            config["Endpoint"].GetString().ShouldBe("ble://AABBCCDDEEFF/random"); commits.ShouldBe(1);
        });
    }

    [Theory]
    [InlineData("ble-adapter://default")]
    [InlineData("BLE-ADAPTER://DEFAULT/")]
    [InlineData("ble-adapter://removed")]
    public async Task BLE_adapter_selector_keeps_saved_identity_until_default_is_chosen(string endpoint)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson(new JsonObject { ["Resources"] = new JsonObject { ["radio"] = new JsonObject { ["Type"] = "ble.adapter", ["Endpoint"] = endpoint, ["Future"] = 42 } },
            ["Workflows"] = new JsonObject { ["main"] = new JsonObject() } }.ToJsonString());
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo(); var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Selection"] = "Resources:radio" }));
            graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
            components.Items.OfType<MudSelect<string>>().Single().GetState(field => field.Value).ShouldBe(endpoint);
            if (endpoint.Contains("removed"))
            {
                view.ToHtmlString().ShouldContain("only ble-adapter://default");
                scope.ServiceProvider.GetRequiredService<EditorValidationState>().Messages.ShouldBeEmpty();
            }
            var unsupported = endpoint.Contains("removed");
            await components.Items.OfType<MudSelect<string>>().Single().ValueChanged.InvokeAsync(unsupported ? "ble-adapter://default" : endpoint); await Task.Yield();
            var after = graph.ToProjectJson();
            graph.Connections.Single().Settings["Endpoint"]!.GetValue<string>().ShouldBe(unsupported ? "ble-adapter://default" : endpoint);
            graph.Connections.Single().Settings["Future"]!.GetValue<int>().ShouldBe(42);
            if (!unsupported) { after.ShouldBe(before); graph.CanRedo.ShouldBeTrue(); }
            else { graph.Undo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(before); graph.Redo(); await Task.Yield(); graph.ToProjectJson().ShouldBe(after); }
        });
    }
}
