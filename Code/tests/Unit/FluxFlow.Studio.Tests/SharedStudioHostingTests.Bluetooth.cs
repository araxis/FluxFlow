using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Plugins;
using FluxFlow.Studio.UI.Shared.Configuration;
using FluxFlow.Plugins;
using FluxFlow.Studio.UI.Shared.Explorer;
using FluxFlow.Plugins.Bluetooth.Design;
using System.Text.Json;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
using FluxFlow.Studio.UI.Features.Designer.Canvas;

using FluxFlow.Studio.UI.Features.Designer.Components.Expressions;
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
    [InlineData("device", false)]
    [InlineData("Resources.device", false)]
    [InlineData("other.Resources.sensor", false)]
    [InlineData("removed", true)]
    [InlineData("api", true)]
    public async Task Ble_connection_selector_preserves_aliases_and_missing_references(string reference, bool missing)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"device":{"Type":"ble.peripheral"},"api":{"Type":"http.client"}},"Workflows":{"main":{"Resources":{"device":{"Type":"ble.peripheral"}}},"other":{"Resources":{"sensor":{"Type":"ble.peripheral"}}}}}""");
        var before = graph.ToProjectJson();
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionSelect>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ConnectionSelect.Connections)] = graph.Connections.ToArray(),
                [nameof(ConnectionSelect.Value)] = reference,
                [nameof(ConnectionSelect.Workflow)] = "main",
                [nameof(ConnectionSelect.PropertyCell)] = true,
                [nameof(ConnectionSelect.AllowEmpty)] = true,
                [nameof(ConnectionSelect.ResourceType)] = "ble.peripheral",
                [nameof(ConnectionSelect.DiagnosticAddress)] = FluxFlow.Application.Addressing.ComponentAddress.Create("main", "client"),
                [nameof(ConnectionSelect.ValueChanged)] = EventCallback.Factory.Create<string>(this, _ => throw new InvalidOperationException("Rendering must not change a reference."))
            }));
            var items = components.Items.OfType<MudSelectItem<string>>().ToArray();
            items.Count(item => item.Value == reference).ShouldBe(1);
            items.ShouldContain(item => item.Value == "");
            items.ShouldNotContain(item => item.Value == "Resources.api");
            diagnostics.Messages.Count.ShouldBe(missing ? 1 : 0);
            if (missing) view.ToHtmlString().ShouldContain("Missing or incompatible connection");
            else view.ToHtmlString().ShouldNotContain("Missing or incompatible connection");
        });
        graph.ToProjectJson().ShouldBe(before);
    }

    [Fact]
    public async Task Ble_connection_selector_refreshes_missing_and_restored_references_without_edits()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"device":{"Type":"ble.peripheral"}},"Workflows":{"main":{}}}""");
        var resources = graph.Connections.ToArray();
        var before = graph.ToProjectJson();
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionSelect>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ConnectionSelect.Connections)] = Array.Empty<FluxFlow.Authoring.ConfigurationResource>(),
                [nameof(ConnectionSelect.Value)] = "device",
                [nameof(ConnectionSelect.PropertyCell)] = true,
                [nameof(ConnectionSelect.AllowEmpty)] = true,
                [nameof(ConnectionSelect.ResourceType)] = "ble.peripheral",
                [nameof(ConnectionSelect.DiagnosticAddress)] = FluxFlow.Application.Addressing.ComponentAddress.Create("main", "client"),
                [nameof(ConnectionSelect.ValueChanged)] = EventCallback.Factory.Create<string>(this, _ => throw new InvalidOperationException("Refreshing must not change a reference."))
            }));
            var selector = components.Items.OfType<ConnectionSelect>().Single();
            var select = components.Items.OfType<MudSelect<string>>().Single();
            select.Value.ShouldBe("device");
            diagnostics.Messages.Count.ShouldBe(1);
            foreach (var available in new[] { resources, [], resources })
            {
                await selector.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(ConnectionSelect.Connections)] = available
                }));
                select = components.Items.OfType<MudSelect<string>>().Last();
                select.Value.ShouldBe("device");
                select.Error.ShouldBe(available.Length == 0);
                diagnostics.Messages.Count.ShouldBe(available.Length == 0 ? 1 : 0);
                if (available.Length == 0) view.ToHtmlString().ShouldContain("Missing or incompatible connection");
                else view.ToHtmlString().ShouldNotContain("Missing or incompatible connection");
            }
            await selector.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ConnectionSelect.Value)] = "Resources.device"
            }));
            select = components.Items.OfType<MudSelect<string>>().Last();
            select.Value.ShouldBe("Resources.device");
            select.Error.ShouldBeFalse();
            diagnostics.Messages.ShouldBeEmpty();
        });
        graph.ToProjectJson().ShouldBe(before);
    }

    [Fact]
    public async Task Ble_property_connection_selection_preserves_manual_target_and_undo_without_discovery()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"device":{"Type":"ble.peripheral","Endpoint":"ble://010203040506/public"}},"Workflows":{"main":{"client":{"Type":"ble.client","cOnNeCtIoN":"removed","DeviceAddress":"AABBCCDDEEFF","AddressType":"Random","ServiceId":"0000180f-0000-1000-8000-00805f9b34fb","CharacteristicId":"00002a19-0000-1000-8000-00805f9b34fb","Extra":42}}}}""");
        graph.Diagram.SelectModel(graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(), true);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<InteractivePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InteractivePropertyConfigurator.Configuration)] = graph.SelectedNode!.Configuration,
                [nameof(InteractivePropertyConfigurator.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            var selector = components.Items.OfType<ConnectionSelect>().Single();
            selector.Value.ShouldBe("removed");
            await selector.ValueChanged.InvokeAsync("Resources.device");
            var named = graph.ToProjectJson();
            graph.SelectedNode!.Configuration["cOnNeCtIoN"].GetString().ShouldBe("Resources.device");
            await Refresh();
            scope.ServiceProvider.GetRequiredService<EditorValidationState>().Messages.ShouldBeEmpty();
            view.ToHtmlString().ShouldNotContain("aria-label=\"Device address\"");
            view.ToHtmlString().ShouldContain("aria-label=\"Service UUID\"");
            await components.Items.OfType<ConnectionSelect>().Last().ValueChanged.InvokeAsync("");
            await Refresh();
            var direct = graph.ToProjectJson();
            var config = graph.SelectedNode!.Configuration;
            config.ContainsKey("cOnNeCtIoN").ShouldBeFalse();
            config["DeviceAddress"].GetString().ShouldBe("AABBCCDDEEFF");
            config["AddressType"].GetString().ShouldBe("Random");
            config["ServiceId"].GetString().ShouldBe("0000180f-0000-1000-8000-00805f9b34fb");
            config["CharacteristicId"].GetString().ShouldBe("00002a19-0000-1000-8000-00805f9b34fb");
            config["Extra"].GetInt32().ShouldBe(42);
            view.ToHtmlString().ShouldContain("aria-label=\"Device address\"");
            explorer.Scans.ShouldBeEmpty(); explorer.Discoveries.ShouldBeEmpty();
            graph.Undo(); graph.ToProjectJson().ShouldBe(named);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo(); graph.ToProjectJson().ShouldBe(direct);

            Task Refresh() => components.Items.OfType<InteractivePropertyConfigurator>().Single().SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InteractivePropertyConfigurator.Configuration)] = graph.SelectedNode!.Configuration
            }));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ble_target_properties_allow_offline_edits_and_preserve_picker_confirmation_history(bool cancel)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"client":{"Type":"ble.client","DeviceAddress":"010203040506","Extra":42}}}}""");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        graph.Diagram.SelectModel(node, true);
        graph.SelectedNode.ShouldBeSameAs(node);
        graph.ResetHistory();
        var before = graph.ToJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<InteractivePropertyConfigurator>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InteractivePropertyConfigurator.Configuration)] = node.Configuration,
                [nameof(InteractivePropertyConfigurator.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
            }));
            foreach (var (name, value) in new[] { ("DeviceAddress", "AABBCCDDEEFF"), ("ServiceId", "0000180f-0000-1000-8000-00805f9b34fb"), ("CharacteristicId", "00002a19-0000-1000-8000-00805f9b34fb") })
            {
                var option = components.Items.OfType<OptionEditorField>().Last(field => field.Option.Name == name);
                var field = components.Items.OfType<MudTextField<string>>().Last(field => field.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == option.Option.DisplayName);
                await field.ValueChanged.InvokeAsync(value);
                node = graph.SelectedNode!;
                node.Configuration[name].GetString().ShouldBe(value);
                await components.Items.OfType<InteractivePropertyConfigurator>().Single().SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(InteractivePropertyConfigurator.Configuration)] = node.Configuration
                }));
            }
            explorer.Scans.ShouldBeEmpty();
            explorer.Discoveries.ShouldBeEmpty();
            var manuallyEdited = graph.ToJson();
            graph.CanUndo.ShouldBeTrue();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            var opened = false;
            dialogs.DialogInstanceAddedAsync += reference =>
            {
                opened = true;
                reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
                reference.Dismiss(cancel ? DialogResult.Cancel() : DialogResult.Ok(new BleTargetSelection("", "112233445566", BleAddressType.Random,
                    Guid.Parse("0000180a-0000-1000-8000-00805f9b34fb"), Guid.Parse("00002a29-0000-1000-8000-00805f9b34fb"))));
                return Task.CompletedTask;
            };
            await components.Items.OfType<MudIconButton>().Single(button => button.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Select BLE target").OnClick.InvokeAsync(new MouseEventArgs());
            opened.ShouldBeTrue();
            node = graph.SelectedNode!;
            node.Configuration["DeviceAddress"].GetString().ShouldBe(cancel ? "AABBCCDDEEFF" : "112233445566");
            node.Configuration["ServiceId"].GetString().ShouldBe(cancel ? "0000180f-0000-1000-8000-00805f9b34fb" : "0000180a-0000-1000-8000-00805f9b34fb");
            node.Configuration["CharacteristicId"].GetString().ShouldBe(cancel ? "00002a19-0000-1000-8000-00805f9b34fb" : "00002a29-0000-1000-8000-00805f9b34fb");
            await components.Items.OfType<InteractivePropertyConfigurator>().Single().SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InteractivePropertyConfigurator.Configuration)] = node.Configuration
            }));
            components.Items.OfType<MudTextField<string>>().Last(field => field.UserAttributes?.GetValueOrDefault("aria-label")?.ToString() == "Device address").Value.ShouldBe(cancel ? "AABBCCDDEEFF" : "112233445566");
            node.Configuration["Extra"].GetInt32().ShouldBe(42);
            if (cancel) graph.ToJson().ShouldBe(manuallyEdited);
            else
            {
                var selected = graph.ToJson();
                graph.Undo();
                graph.ToJson().ShouldBe(manuallyEdited);
                graph.Redo();
                graph.ToJson().ShouldBe(selected);
                graph.Undo();
            }
            graph.Undo();
            graph.SelectedNode!.Configuration.ContainsKey("CharacteristicId").ShouldBeFalse();
            graph.SelectedNode.Configuration["ServiceId"].GetString().ShouldBe("0000180f-0000-1000-8000-00805f9b34fb");
            graph.Undo();
            graph.Undo();
            graph.ToJson().ShouldBe(before);
        });
    }

    [Fact]
    public async Task Shared_property_grid_honors_visibility_style_for_inspector_tabs()
    {
        var services = CreateServices();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<FluxFlow.Studio.UI.Components.Grid.StudioPropertyGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Style"] = "display:none", ["ChildContent"] = (RenderFragment)(builder => builder.AddMarkupContent(0, "<tbody><tr><td>Property</td></tr></tbody>"))
            }));
            view.ToHtmlString().ShouldContain("display:none");
        });
    }

    [Fact]
    public async Task Ble_property_diagnostic_links_to_validation_and_removes_repaired_records_without_editing_graph()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        var diagnostics = scope.ServiceProvider.GetRequiredService<EditorValidationState>();
        var graph = scope.ServiceProvider.GetRequiredService<FluxFlow.Studio.UI.Features.Designer.DesignerGraphState>();
        var before = graph.ToProjectJson();
        var revision = graph.Revision;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<BinaryValueEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BinaryValueEditor.Format)] = BleValueFormat.For(),
                [nameof(BinaryValueEditor.Value)] = BleValueFormat.ToEditor(new BlePayload { Encoding = BleValueEncoding.Hex, Data = "0Z" }),
                [nameof(BinaryValueEditor.PropertyRows)] = true,
                [nameof(BinaryValueEditor.DiagnosticAddress)] = FluxFlow.Application.Addressing.ComponentAddress.Create("main", "client")
            }));
            var entry = diagnostics.Messages.Single();
            entry.Value.Path.ShouldBe("Value");
            entry.Value.Address!.Value.ShouldBe("main.client");
            System.Text.RegularExpressions.Regex.Replace(view.ToHtmlString(), "<[^>]*>", "").ShouldNotContain("Enter valid hexadecimal");
            var indicator = components.Items.OfType<MudIconButton>().Single();
            indicator.UserAttributes!["title"].ShouldBeOfType<string>().ShouldContain("Enter valid hexadecimal");
            indicator.UserAttributes["aria-description"].ShouldBeOfType<string>().ShouldContain("Enter valid hexadecimal");
            await indicator.OnClick.InvokeAsync(new MouseEventArgs());
            diagnostics.Selected.ShouldBe(entry.Key);
            await components.Items.OfType<BinaryValueEditor>().Single().SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BinaryValueEditor.Format)] = BleValueFormat.For(),
                [nameof(BinaryValueEditor.Value)] = BleValueFormat.ToEditor(new BlePayload { Encoding = BleValueEncoding.Hex, Data = "00FF" })
            }));
            diagnostics.Messages.ShouldBeEmpty();
            diagnostics.Selected.ShouldBeNull();
            await indicator.OnClick.InvokeAsync(new MouseEventArgs());
            diagnostics.Selected.ShouldBeNull();
        });
        graph.ToProjectJson().ShouldBe(before);
        graph.Revision.ShouldBe(revision);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Ble_UUID_picker_stages_both_UUIDs_and_rejects_canceled_or_stale_selection(bool cancel, bool stale)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var explorer = new PickerExplorer();
        services.AddSingleton<IBleExplorer>(explorer);
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var service = Guid.NewGuid(); var characteristic = Guid.NewGuid(); var current = true;
            var dialog = await OpenPicker(dialogs, explorer, initial: new BleTargetSelection("", "010203040506", BleAddressType.Random), current: () => current);
            explorer.Scans.ShouldBeEmpty(); explorer.Discoveries.ShouldBeEmpty();
            var pending = EnterPickerDevice(components, new BleDeviceInfo("010203040506", BleAddressType.Random, "Test peripheral", -40));
            explorer.Discoveries.Single().Options.DeviceAddress.ShouldBe("010203040506");
            var otherService = Guid.NewGuid();
            explorer.Discoveries.Single().Result.SetResult([new(service, [new(characteristic, true, "Read")]), new(otherService, [])]);
            await pending;
            var tree = components.Items.OfType<MudTreeView<ExplorerSelection>>().Last(item => item.UserAttributes.ContainsKey("aria-label"));
            await tree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            dialog.Result.IsCompleted.ShouldBeFalse();
            var confirm = components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary);
            confirm.Disabled.ShouldBeFalse();
            await tree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(otherService, null));
            confirm.Disabled.ShouldBeTrue();
            await confirm.OnClick.InvokeAsync(new MouseEventArgs());
            dialog.Result.IsCompleted.ShouldBeFalse();
            await tree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, null));
            confirm.Disabled.ShouldBeTrue();
            await tree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            if (cancel) await components.Items.OfType<MudButton>().Single(button => button.Color != Color.Primary && button.StartIcon is null && button.Class is null).OnClick.InvokeAsync(new MouseEventArgs());
            if (stale) current = false;
            await confirm.OnClick.InvokeAsync(new MouseEventArgs());
            if (stale) { dialog.Result.IsCompleted.ShouldBeFalse(); dialog.Close(DialogResult.Cancel()); }
            var result = (await dialog.Result)!;
            if (cancel || stale) result.Canceled.ShouldBeTrue();
            else result.Data.ShouldBe(new BleTargetSelection("", "010203040506", BleAddressType.Random, service, characteristic));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ble_value_editor_rejects_lossy_encoding_without_changing_bytes_or_selected_encoding(bool propertyRows)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var original = new BlePayload { Encoding = BleValueEncoding.Hex, Data = "00FF" };
            var changes = new List<BlePayload>();
            var view = await renderer.RenderComponentAsync<BinaryValueEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BinaryValueEditor.Format)] = BleValueFormat.For(),
                [nameof(BinaryValueEditor.Value)] = BleValueFormat.ToEditor(original),
                [nameof(BinaryValueEditor.PropertyRows)] = propertyRows,
                [nameof(BinaryValueEditor.ValueChanged)] = EventCallback.Factory.Create<EncodedValue>(this, value => changes.Add(BleValueFormat.FromEditor(value)))
            }));
            await components.Items.OfType<MudSelect<string>>().Last().SelectOption("Utf8");
            changes.ShouldBeEmpty();
            components.Items.OfType<MudSelect<string>>().Last().Value.ShouldBe("Hex");
            original.Decode().ShouldBe(new byte[] { 0, 255 });
            view.ToHtmlString().ShouldContain("These bytes cannot be represented as UTF-8");
            if (propertyRows)
            {
                var input = components.Items.OfType<MudTextField<string>>().Single();
                await input.ValueChanged.InvokeAsync("0Z");
                view.ToHtmlString().ShouldContain("Enter valid hexadecimal pairs");
                await components.Items.OfType<MudSelect<string>>().Last().SelectOption("Base64");
                changes.ShouldBeEmpty();
                await input.ValueChanged.InvokeAsync("0102");
                changes.Single().Decode().ShouldBe(new byte[] { 1, 2 });
                changes.Clear();
            }
            await components.Items.OfType<MudSelect<string>>().Last().SelectOption("Base64");
            changes.Single().Data.ShouldBe("AP8=");
            changes.Single().Decode().ShouldBe(original.Decode());
        });
    }

    [Theory]
    [InlineData(BleValueEncoding.Hex, "0Z", false)]
    [InlineData(BleValueEncoding.Hex, "0Z", true)]
    [InlineData(BleValueEncoding.Base64, "!not base64!", false)]
    [InlineData(BleValueEncoding.Base64, "!not base64!", true)]
    [InlineData(BleValueEncoding.Hex, "oversized", false)]
    [InlineData(BleValueEncoding.Hex, "oversized", true)]
    public async Task Ble_value_editor_shows_payload_diagnostics_and_clears_them_after_repair(BleValueEncoding encoding, string data, bool propertyRows)
    {
        if (data == "oversized") data = new string('A', 1026);
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var value = new BlePayload { Encoding = encoding, Data = data };
            var invalid = Should.Throw<Exception>(() => value.Decode());
            var error = invalid is FormatException
                ? encoding == BleValueEncoding.Hex ? "Enter valid hexadecimal pairs (for example, 00FF)." : "Enter a valid Base64 value."
                : invalid.Message;
            var view = await renderer.RenderComponentAsync<BinaryValueEditor>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BinaryValueEditor.Format)] = BleValueFormat.For(),
                [nameof(BinaryValueEditor.Value)] = BleValueFormat.ToEditor(value),
                [nameof(BinaryValueEditor.PropertyRows)] = propertyRows
            }));
            System.Net.WebUtility.HtmlDecode(view.ToHtmlString()).ShouldContain(error);
            value.Data.ShouldBe(data);
            await components.Items.OfType<BinaryValueEditor>().Single().SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BinaryValueEditor.Format)] = BleValueFormat.For(),
                [nameof(BinaryValueEditor.Value)] = BleValueFormat.ToEditor(new BlePayload { Encoding = BleValueEncoding.Hex, Data = "00FF" })
            }));
            var html = System.Net.WebUtility.HtmlDecode(view.ToHtmlString());
            html.ShouldNotContain(error);
            html.ShouldContain("2 / 512 bytes");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ble_device_dialog_selection_requires_OK_and_Cancel_discards_it(bool cancel)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference =>
            {
                reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
                return Task.CompletedTask;
            };
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var dialog = await OpenPicker(dialogs, explorer, deviceOnly: true);
            explorer.Scans.ShouldBeEmpty(); explorer.Discoveries.ShouldBeEmpty();
            components.Items.OfType<MudTextField<string>>().Single(field => field.Label == "Device address").Value.ShouldBe("");
            var scan = components.Items.OfType<MudButton>().Single(button => button.UserAttributes.GetValueOrDefault("aria-label") as string == "Scan devices");
            var pending = scan.OnClick.InvokeAsync(new MouseEventArgs());
            var device = new BleDeviceInfo("010203040506", BleAddressType.Random, "Test peripheral", -40);
            explorer.Scans[0].Result.SetResult([device]);
            await pending;
            var tree = components.Items.OfType<MudTreeView<ExplorerSelection>>().Last(item => item.UserAttributes.ContainsKey("aria-label"));
            await tree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(Guid.Empty, null, device));
            dialog.Result.IsCompleted.ShouldBeFalse();
            var confirm = components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary);
            confirm.Disabled.ShouldBeFalse();
            if (cancel)
            {
                await components.Items.OfType<MudButton>().Single(button => button.StartIcon is null && button.Color != Color.Primary).OnClick.InvokeAsync(new MouseEventArgs());
                (await dialog.Result)!.Canceled.ShouldBeTrue();
                await confirm.OnClick.InvokeAsync(new MouseEventArgs());
                (await dialog.Result)!.Canceled.ShouldBeTrue();
            }
            else
            {
                await confirm.OnClick.InvokeAsync(new MouseEventArgs());
                (await dialog.Result)!.Data.ShouldBe(new BleTargetSelection("", device.Address, device.AddressType));
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ble_device_dialog_cancel_or_disposal_cancels_scan_and_rejects_late_results(bool dispose)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components);
        services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference =>
            {
                reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
                return Task.CompletedTask;
            };
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var dialog = await OpenPicker(dialogs, explorer, deviceOnly: true);
            var pending = components.Items.OfType<MudButton>().Single(button => button.UserAttributes.GetValueOrDefault("aria-label") as string == "Scan devices").OnClick.InvokeAsync(new MouseEventArgs());
            if (dispose) components.Items.OfType<ExplorerPickerDialog>().Single().Dispose();
            else await components.Items.OfType<MudButton>().Single(button => button.StartIcon is null && button.Color != Color.Primary).OnClick.InvokeAsync(new MouseEventArgs());
            explorer.Scans[0].Token.IsCancellationRequested.ShouldBeTrue();
            explorer.Scans[0].Result.SetResult([new("010203040506", BleAddressType.Random, "Late device", -40)]);
            await pending;
            components.Items.OfType<MudTreeViewItem<ExplorerSelection>>().Where(item => item.Value is BleTargetBrowser.GattSelection { Device: not null }).ShouldBeEmpty();
            if (dispose) dialog.Close(DialogResult.Cancel());
            (await dialog.Result)!.Canceled.ShouldBeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ble_target_picker_rejects_late_discovery_after_target_change_or_cancel(bool cancel)
    {
        var services = CreateServices(); var components = new CapturingComponents(); var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components); services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>(); await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await OpenPicker(dialogs, explorer, initial: new BleTargetSelection("", "010203040506", BleAddressType.Public));
            var pending = components.Items.OfType<MudIconButton>().Single(button => button.Icon == Icons.Material.Outlined.Refresh).OnClick.InvokeAsync(new MouseEventArgs());
            if (cancel) await components.Items.OfType<MudButton>().Single(button => button.Color != Color.Primary && button.StartIcon is null).OnClick.InvokeAsync(new MouseEventArgs());
            else await components.Items.OfType<MudTextField<string>>().Single(field => field.Label == "Device address").ValueChanged.InvokeAsync("AABBCCDDEEFF");
            explorer.Discoveries.Single().Token.IsCancellationRequested.ShouldBeTrue();
            explorer.Discoveries.Single().Result.SetResult([new(Guid.NewGuid(), [new(Guid.NewGuid(), true, "Read")])]);
            await pending;
            components.Items.OfType<MudTreeViewItem<ExplorerSelection>>().Where(item => item.Value is BleTargetBrowser.GattSelection { CharacteristicId: not null }).ShouldBeEmpty();
            if (!cancel) { components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary).Disabled.ShouldBeTrue(); dialog.Close(DialogResult.Cancel()); }
            (await dialog.Result)!.Canceled.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Ble_named_target_resolution_failure_never_uses_the_hidden_direct_address()
    {
        var services = CreateServices(); var components = new CapturingComponents(); var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components); services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await OpenPicker(dialogs, explorer, initial: new BleTargetSelection("device", "010203040506", BleAddressType.Public), resolve: (Func<string, BleClientOptions>)(_ => throw new ArgumentException("Connection unavailable")));
            view.ToHtmlString().ShouldContain("Connection unavailable");
            components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary).Disabled.ShouldBeTrue();
            var connect = components.Items.OfType<MudIconButton>().Single(button => button.Icon == Icons.Material.Outlined.Refresh);
            connect.Disabled.ShouldBeTrue();
            await connect.OnClick.InvokeAsync(new MouseEventArgs());
            explorer.Scans.ShouldBeEmpty(); explorer.Discoveries.ShouldBeEmpty();
            dialog.Close(DialogResult.Cancel());
        });
    }

    [Fact]
    public async Task Ble_target_picker_auto_discovery_replaces_pending_requests_and_rejects_old_tree_selection()
    {
        var services = CreateServices(); var components = new CapturingComponents(); var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components); services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>(); await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await OpenPicker(dialogs, explorer);
            var picker = components.Items.OfType<MudTextField<string>>().Single(field => field.Label == "Device address");
            var first = EnterPickerDevice(components, new BleDeviceInfo("010203040506", BleAddressType.Public, "First", -40));
            var second = EnterPickerDevice(components, new BleDeviceInfo("AABBCCDDEEFF", BleAddressType.Random, "Second", -40));
            explorer.Discoveries[0].Token.IsCancellationRequested.ShouldBeTrue();
            explorer.Discoveries[1].Options.AddressType.ShouldBe(BleAddressType.Random);
            var service = Guid.NewGuid(); var characteristic = Guid.NewGuid();
            explorer.Discoveries[1].Result.SetResult([new(service, [new(characteristic, true, "Read, Notify")])]);
            await second;
            explorer.Discoveries[0].Result.SetResult([new(Guid.NewGuid(), [])]);
            await first;
            var tree = components.Items.OfType<MudTreeView<ExplorerSelection>>().Last(item => item.UserAttributes.ContainsKey("aria-label"));
            await tree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            var confirm = components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary);
            confirm.Disabled.ShouldBeFalse();
            await picker.OnBlur.InvokeAsync(new FocusEventArgs());
            explorer.Discoveries.Count.ShouldBe(2);
            var refresh = components.Items.OfType<MudIconButton>().Single(button => button.Icon == Icons.Material.Outlined.Refresh);
            var retry = refresh.OnClick.InvokeAsync(new MouseEventArgs());
            confirm.Disabled.ShouldBeTrue();
            explorer.Discoveries[2].Result.SetResult([new(service, [new(characteristic, true, "Read, Notify")])]);
            await retry;
            await tree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            confirm.Disabled.ShouldBeTrue();
            var currentTree = components.Items.OfType<MudTreeView<ExplorerSelection>>().Last(item => item.UserAttributes.ContainsKey("aria-label"));
            await currentTree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            confirm.Disabled.ShouldBeFalse();
            await confirm.OnClick.InvokeAsync(new MouseEventArgs());
            (await dialog.Result)!.Data.ShouldBe(new BleTargetSelection("", "AABBCCDDEEFF", BleAddressType.Random, service, characteristic));
        });
    }

    [Fact]
    public async Task Ble_target_picker_discovers_named_connections_and_cancels_on_source_change()
    {
        var services = CreateServices(); var components = new CapturingComponents(); var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components); services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>(); await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await OpenPicker(dialogs, explorer, resolve: (Func<string, BleClientOptions>)(_ => new() { DeviceAddress = "010203040506", AddressType = BleAddressType.Random }));
            explorer.Discoveries.ShouldBeEmpty();
            var direct = EnterPickerDevice(components, new BleDeviceInfo("AABBCCDDEEFF", BleAddressType.Public, "Direct device", -40));
            explorer.Discoveries.Single().Result.SetResult([]);
            await direct;
            await components.Items.OfType<MudSelect<string>>().Single(field => field.UserAttributes.ContainsKey("aria-label") && Equals(field.UserAttributes["aria-label"], "Device source")).ValueChanged.InvokeAsync("connection");
            var pending = components.Items.OfType<MudSelect<string>>().Single(field => field.Label == "Peripheral connection").ValueChanged.InvokeAsync("peripheral");
            explorer.Discoveries.Last().Options.AddressType.ShouldBe(BleAddressType.Random);
            var root = components.Items.OfType<MudTreeViewItem<ExplorerSelection>>().Last(item => item.Value is BleTargetBrowser.GattSelection { Device: not null });
            root.Value.ShouldBeOfType<BleTargetBrowser.GattSelection>().Device!.Address.ShouldBe("010203040506");
            root.Value.ShouldBeOfType<BleTargetBrowser.GattSelection>().Device!.AddressType.ShouldBe(BleAddressType.Random);
            var namedRequest = explorer.Discoveries.Last();
            var returnToDirect = components.Items.OfType<MudSelect<string>>().Single(field => Equals(field.UserAttributes.GetValueOrDefault("aria-label"), "Device source")).ValueChanged.InvokeAsync("direct");
            namedRequest.Token.IsCancellationRequested.ShouldBeTrue();
            namedRequest.Result.SetResult([new(Guid.NewGuid(), [])]);
            await pending;
            explorer.Discoveries.Last().Result.SetResult([]);
            await returnToDirect;
            components.Items.OfType<MudTreeViewItem<ExplorerSelection>>().Where(item => item.Value is BleTargetBrowser.GattSelection { CharacteristicId: not null }).ShouldBeEmpty();
            components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary).Disabled.ShouldBeTrue();
            dialog.Close(DialogResult.Cancel());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ble_target_picker_canceled_discovery_cannot_replace_retry_or_inspector(bool lateFailure)
    {
        var services = CreateServices(); var components = new CapturingComponents(); var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components); services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await OpenPicker(dialogs, explorer, initial: new BleTargetSelection("", "010203040506", BleAddressType.Public));
            var refresh = components.Items.OfType<MudIconButton>().Single(button => button.Icon == Icons.Material.Outlined.Refresh);
            var canceled = refresh.OnClick.InvokeAsync(new MouseEventArgs());
            var oldTree = components.Items.OfType<MudTreeView<ExplorerSelection>>().Last(item => item.UserAttributes.ContainsKey("aria-label"));
            await refresh.OnClick.InvokeAsync(new MouseEventArgs());
            explorer.Discoveries[0].Token.IsCancellationRequested.ShouldBeTrue();
            var confirm = components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary);
            confirm.Disabled.ShouldBeTrue();
            var retry = refresh.OnClick.InvokeAsync(new MouseEventArgs());
            var service = Guid.NewGuid(); var characteristic = Guid.NewGuid();
            if (lateFailure) explorer.Discoveries[0].Result.SetException(new InvalidOperationException("Late canceled discovery"));
            else explorer.Discoveries[0].Result.SetResult([new(service, [new(characteristic, true, "Old properties")])]);
            await canceled;
            confirm.Disabled.ShouldBeTrue();
            refresh.UserAttributes["aria-label"].ShouldBe("Cancel discovery");
            explorer.Discoveries[1].Result.SetResult([new(service, [new(characteristic, true, "Read, Notify")])]);
            await retry;
            await oldTree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            confirm.Disabled.ShouldBeTrue();
            var currentTree = components.Items.OfType<MudTreeView<ExplorerSelection>>().Last(item => item.UserAttributes.ContainsKey("aria-label"));
            await currentTree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            var html = view.ToHtmlString();
            html.ShouldContain("Selected characteristic"); html.ShouldContain("Read, Notify");
            html.ShouldNotContain("Old properties"); html.ShouldNotContain("Late canceled discovery");
            confirm.Disabled.ShouldBeFalse();
            await confirm.OnClick.InvokeAsync(new MouseEventArgs());
            (await dialog.Result)!.Data.ShouldBe(new BleTargetSelection("", "010203040506", BleAddressType.Public, service, characteristic));
        });
    }

    [Fact]
    public async Task Ble_target_tree_discovers_only_selected_device_and_keeps_selection_with_its_parent()
    {
        var services = CreateServices(); var components = new CapturingComponents(); var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components); services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var dialog = await OpenPicker(dialogs, explorer);
            var first = new BleDeviceInfo("010203040506", BleAddressType.Public, "First", -40);
            var second = new BleDeviceInfo("AABBCCDDEEFF", BleAddressType.Random, "Second", -50);
            var scan = components.Items.OfType<MudButton>().Single(button => button.UserAttributes.GetValueOrDefault("aria-label") as string == "Scan devices").OnClick.InvokeAsync(new MouseEventArgs());
            explorer.Scans.Single().Result.SetResult([first, second]);
            await scan;
            explorer.Discoveries.ShouldBeEmpty();
            MudTreeView<ExplorerSelection> Tree() => components.Items.OfType<MudTreeView<ExplorerSelection>>().Last(item => item.UserAttributes.ContainsKey("aria-label"));
            var pendingFirst = Tree().SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(Guid.Empty, null, first));
            var oldTree = Tree();
            var pendingSecond = Tree().SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(Guid.Empty, null, second));
            explorer.Discoveries[0].Token.IsCancellationRequested.ShouldBeTrue();
            var service = Guid.NewGuid(); var characteristic = Guid.NewGuid();
            explorer.Discoveries[1].Result.SetResult([new(service, [new(characteristic, true, "Read, Notify")])]);
            await pendingSecond;
            explorer.Discoveries[0].Result.SetResult([new(service, [new(characteristic, true, "Read")])]);
            await pendingFirst;
            var confirm = components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary);
            await oldTree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            confirm.Disabled.ShouldBeTrue();
            await Tree().SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            confirm.Disabled.ShouldBeFalse();
            var html = view.ToHtmlString();
            html.ShouldContain("Target details");
            html.ShouldContain("Selected characteristic");
            html.ShouldContain(characteristic.ToString());
            html.ShouldContain("Read, Notify");
            html.ShouldContain(second.Address);
            await Tree().SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(Guid.Empty, null, second));
            html = view.ToHtmlString();
            html.ShouldNotContain("Selected characteristic");
            html.ShouldNotContain("<th scope=\"row\">Characteristic UUID</th>");
            confirm.Disabled.ShouldBeTrue();
            await Tree().SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            await confirm.OnClick.InvokeAsync(new MouseEventArgs());
            var selected = ((await dialog.Result)!.Data as BleTargetSelection)!;
            selected.DeviceAddress.ShouldBe(second.Address);
            selected.AddressType.ShouldBe(second.AddressType);
            selected.ServiceId.ShouldBe(service); selected.CharacteristicId.ShouldBe(characteristic);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ble_target_tree_can_discover_current_device_during_or_after_canceling_a_slow_scan(bool cancelFirst)
    {
        var services = CreateServices(); var components = new CapturingComponents(); var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components); services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>(); await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var device = new BleDeviceInfo("010203040506", BleAddressType.Public, "Device", 0);
            var dialog = await OpenPicker(dialogs, explorer, initial: new BleTargetSelection("", device.Address, device.AddressType));
            var scanButton = components.Items.OfType<MudButton>().Single(button => button.UserAttributes.GetValueOrDefault("aria-label") as string == "Scan devices");
            var scan = scanButton.OnClick.InvokeAsync(new MouseEventArgs());
            if (cancelFirst) await scanButton.OnClick.InvokeAsync(new MouseEventArgs());
            var tree = components.Items.OfType<MudTreeView<ExplorerSelection>>().Last(item => item.UserAttributes.ContainsKey("aria-label"));
            var discovery = tree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(Guid.Empty, null, device));
            explorer.Scans.Single().Token.IsCancellationRequested.ShouldBeTrue();
            explorer.Discoveries.Count.ShouldBe(1);
            var service = Guid.NewGuid(); var characteristic = Guid.NewGuid();
            explorer.Discoveries.Single().Result.SetResult([new(service, [new(characteristic, true, "Read")])]);
            await discovery;
            explorer.Scans.Single().Result.SetException(new InvalidOperationException("Late scan failure"));
            await scan;
            var currentTree = components.Items.OfType<MudTreeView<ExplorerSelection>>().Last(item => item.UserAttributes.ContainsKey("aria-label"));
            await currentTree.SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
            var confirm = components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary);
            confirm.Disabled.ShouldBeFalse();
            await confirm.OnClick.InvokeAsync(new MouseEventArgs());
            (await dialog.Result)!.Data.ShouldBe(new BleTargetSelection("", device.Address, device.AddressType, service, characteristic));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ble_target_tree_device_selection_matches_the_confirmed_target(bool deviceOnly)
    {
        var services = CreateServices(); var components = new CapturingComponents(); var explorer = new PickerExplorer();
        services.AddSingleton<IComponentActivator>(components); services.AddSingleton<IBleExplorer>(explorer);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>(); await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference => { reference.RenderCompleteTaskCompletionSource.TrySetResult(true); return Task.CompletedTask; };
            var device = new BleDeviceInfo("010203040506", BleAddressType.Public, "Device", 0);
            var dialog = await OpenPicker(dialogs, explorer, initial: new BleTargetSelection("", device.Address, device.AddressType), deviceOnly: deviceOnly);
            MudTreeView<ExplorerSelection> Tree() => components.Items.OfType<MudTreeView<ExplorerSelection>>().Last(item => item.UserAttributes.ContainsKey("aria-label"));
            var discovery = Tree().SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(Guid.Empty, null, device));
            if (!deviceOnly)
            {
                var service = Guid.NewGuid(); var characteristic = Guid.NewGuid();
                explorer.Discoveries.Single().Result.SetResult([new(service, [new(characteristic, true, "Read")])]);
                await discovery;
                await Tree().SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(service, characteristic));
                components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary).Disabled.ShouldBeFalse();
            }
            await discovery;
            await Tree().SelectedValueChanged.InvokeAsync(new BleTargetBrowser.GattSelection(Guid.Empty, null, device));
            components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary).Disabled.ShouldBe(!deviceOnly);
            if (deviceOnly) { Tree().SelectedValue.ShouldBeOfType<BleTargetBrowser.GattSelection>().Device.ShouldBe(device); explorer.Discoveries.ShouldBeEmpty(); }
            else explorer.Discoveries.Count.ShouldBe(1);
            dialog.Close(DialogResult.Cancel());
        });
    }

    private static Task<IDialogReference> OpenPicker(IDialogService dialogs, IBleExplorer explorer, BleTargetSelection? initial = null,
        bool deviceOnly = false, Func<string, BleClientOptions>? resolve = null, Func<bool>? current = null) =>
        dialogs.ShowAsync<ExplorerPickerDialog>("Select target", new DialogParameters<ExplorerPickerDialog>
        {
            { item => item.Session, new BleTargetBrowser(explorer, initial ?? new("", "", BleAddressType.Public), false,
                deviceOnly, resolve, _ => null, current) }
        });

    private static async Task EnterPickerDevice(CapturingComponents components, BleDeviceInfo device)
    {
        await components.Items.OfType<MudTextField<string>>().Single(field => field.Label == "Device address").ValueChanged.InvokeAsync(device.Address);
        await components.Items.OfType<MudSelect<string>>().Single(field => field.Label == "Address type").ValueChanged.InvokeAsync(device.AddressType.ToString());
    }

    private sealed class PickerExplorer : IBleExplorer
    {
        public List<(CancellationToken Token, TaskCompletionSource<BleDeviceInfo[]> Result)> Scans { get; } = [];
        public List<(BleClientOptions Options, CancellationToken Token, TaskCompletionSource<BleServiceInfo[]> Result)> Discoveries { get; } = [];
        public Task<BleDeviceInfo[]> ScanAsync(CancellationToken cancellation)
        {
            var result = new TaskCompletionSource<BleDeviceInfo[]>();
            Scans.Add((cancellation, result));
            return result.Task;
        }
        public Task<BleServiceInfo[]> DiscoverAsync(BleClientOptions options, CancellationToken cancellation)
        {
            var result = new TaskCompletionSource<BleServiceInfo[]>();
            Discoveries.Add((options, cancellation, result));
            return result.Task;
        }
        public Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ble_server_invalid_saved_configuration_opens_in_json_for_repair_or_cancel(bool cancel)
    {
        var original = """{"Type":"ble.server","Characteristics":false,"Extra":42}""";
        var corrected = """
            {"Type":"ble.server","ServiceId":"3f63b38e-98df-4a52-a9f8-76b803a934b2",
             "Characteristics":[{"Id":"f50d7529-4e5d-4088-b88f-e236cd1e7f29","Value":"repaired"}],"Extra":42}
            """;
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudDialogProvider>();
            var dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
            dialogs.DialogInstanceAddedAsync += reference =>
            {
                reference.RenderCompleteTaskCompletionSource.TrySetResult(true);
                return Task.CompletedTask;
            };
            var dialog = await dialogs.ShowAsync<StructuredSettingsDialog>("BLE GATT server",
                new DialogParameters<StructuredSettingsDialog> { { field => field.Value, original }, { field => field.Hierarchy, BleGattDocument.Editor } });
            var editor = components.Items.OfType<ExpressionCodeEditor>().Single();
            editor.Value.ShouldBe(original);
            var save = components.Items.OfType<MudButton>().Single(button => button.Color == Color.Primary);
            await save.OnClick.InvokeAsync(new MouseEventArgs());
            dialog.Result.IsCompleted.ShouldBeFalse();
            editor.Value.ShouldBe(original);
            await editor.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(editor.Value)] = corrected }));
            if (cancel)
            {
                await components.Items.OfType<MudButton>().Single(button => button.Color != Color.Primary).OnClick.InvokeAsync(new MouseEventArgs());
                (await dialog.Result)!.Canceled.ShouldBeTrue();
                // A delayed or duplicate Save must not replace the cancellation result.
                await save.OnClick.InvokeAsync(new MouseEventArgs());
                (await dialog.Result)!.Canceled.ShouldBeTrue();
            }
            else
            {
                await save.OnClick.InvokeAsync(new MouseEventArgs());
                var result = (await dialog.Result)!;
                result.Canceled.ShouldBeFalse();
                var value = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(result.Data.ShouldBeOfType<string>())!;
                value["Characteristics"][0].GetProperty("Value").GetString().ShouldBe("repaired");
                value["Extra"].GetInt32().ShouldBe(42);
            }
        });
    }
}

