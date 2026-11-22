using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Plugins.Bluetooth.Design;
using System.Text.Json;
using FluxFlow.Components.Bluetooth;

using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class BleClientEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Target_selection_is_one_undoable_edit_and_command_mode_preserves_UUIDs(bool command)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"client\":" + Original + "}}}");
        var node = graph.Diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Designer.Canvas.FlowNodeModel>().Single();
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var service = Guid.NewGuid(); var characteristic = Guid.NewGuid();
        using (graph.BeginManualEdit())
        {
            new BleTargetSelection("", "AABBCCDDEEFF", BleAddressType.Random,
                command ? null : service, command ? null : characteristic).Apply(node.Configuration);
            graph.RecordEdit();
        }
        node.Configuration["ServiceId"].GetString().ShouldBe(command ? "3f63b38e-98df-4a52-a9f8-76b803a934b2" : service.ToString());
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public void Named_target_uses_environment_snapshot_and_rejects_ambiguous_or_remote_discovery()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"device":{"Type":"ble.peripheral","Endpoint":"ble://010203040506/public"}},"Workflows":{"main":{}}}""");
        var environment = graph.GetEnvironmentDefinition();
        var before = graph.ToProjectJson();
        var connection = graph.Connections.Single();
        var host = new FluxFlow.Environments.EnvironmentNode { Id = "host", Kind = FluxFlow.Environments.EnvironmentNodeKind.Host, LocalHost = true };
        var service = new FluxFlow.Environments.EnvironmentNode { Id = "peripheral", Kind = FluxFlow.Environments.EnvironmentNodeKind.Service, Host = host.Id,
            ExistingService = new() { Type = "ble.peripheral", Endpoint = "ble://AABBCCDDEEFF/random" } };
        var application = new FluxFlow.Environments.EnvironmentNode { Id = "app", Kind = FluxFlow.Environments.EnvironmentNodeKind.Application, Host = host.Id,
            Bindings = new() { [connection.Path] = service.Id } };
        environment.Topology.Nodes.AddRange([host, service, application]);
        var target = BleDiscoveryAddress.Resolve(DiscoveryEndpoints.Resolve(graph.Connections.ToArray(), environment, "device", "main", "ble.peripheral"));
        target.Address.ShouldBe(0xAABBCCDDEEFFUL); target.AddressType.ShouldBe(BleAddressType.Random);
        environment.Topology.Nodes.Add(new() { Id = "second", Kind = FluxFlow.Environments.EnvironmentNodeKind.Application });
        Should.Throw<ArgumentException>(() => BleDiscoveryAddress.Resolve(DiscoveryEndpoints.Resolve(graph.Connections.ToArray(), environment, "device", "main", "ble.peripheral"))).Message.ShouldContain("different devices");
        environment.Topology.Nodes.RemoveAt(environment.Topology.Nodes.Count - 1);
        application.Host = "remote";
        Should.Throw<ArgumentException>(() => BleDiscoveryAddress.Resolve(DiscoveryEndpoints.Resolve(graph.Connections.ToArray(), environment, "device", "main", "ble.peripheral"))).Message.ShouldContain("remote host");
        Should.Throw<ArgumentException>(() => BleDiscoveryAddress.Resolve(DiscoveryEndpoints.Resolve(graph.Connections.ToArray(), environment, "missing", "main", "ble.peripheral")));
        graph.ToProjectJson().ShouldBe(before);
    }

    [Fact]
    public void Named_connection_picker_resolves_existing_aliases_and_explicit_scopes_without_rewriting_them()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"device":{"Type":"ble.peripheral","Endpoint":"ble://010203040506/public"},"api":{"Type":"http.client"}},"Workflows":{"main":{"Resources":{"device":{"Type":"ble.peripheral","Endpoint":"ble://AABBCCDDEEFF/random"}}},"other":{"Resources":{"sensor":{"Type":"ble.peripheral","Endpoint":"ble://010203040507/public"}}}}}""");
        var before = graph.ToProjectJson();
        ConnectionSelection.Resolve(graph.Connections, "ble.peripheral", "device", "main")!.Address.ShouldBe("main.Resources.device");
        ConnectionSelection.Resolve(graph.Connections, "ble.peripheral", "Resources.device", "main")!.Address.ShouldBe("Resources.device");
        ConnectionSelection.Resolve(graph.Connections, "ble.peripheral", "other.Resources.sensor", "main")!.Address.ShouldBe("other.Resources.sensor");
        ConnectionSelection.Resolve(graph.Connections, "ble.peripheral", "api", "main").ShouldBeNull();
        ConnectionSelection.Resolve(graph.Connections, "ble.peripheral", "missing", "main").ShouldBeNull();
        graph.ToProjectJson().ShouldBe(before);
    }

    [Fact]
    public void Input_mode_and_device_source_edits_preserve_fixed_values_and_restore_connection_with_undo()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"client\":" + Original + "}}}");
        var node = graph.Diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Designer.Canvas.FlowNodeModel>().Single();
        BleClientProperties.Set(node.Configuration, "Connection", "Resources.device");
        graph.RecordEdit(); graph.ResetHistory();
        var before = graph.ToProjectJson();
        BleClientProperties.Set(node.Configuration, "UseInputOperation", true);
        BleClientProperties.Set(node.Configuration, "Connection", "");
        node.Configuration["ServiceId"].GetString().ShouldBe("3f63b38e-98df-4a52-a9f8-76b803a934b2");
        node.Configuration["deviceaddress"].GetString().ShouldBe("010203040506");
        graph.RecordEdit();
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public void Property_grid_visibility_keeps_hidden_settings_and_notification_targets()
    {
        var configuration = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Original)!;
        configuration["Operation"] = JsonSerializer.SerializeToElement("Write");
        configuration["Value"] = JsonSerializer.SerializeToElement(new { Encoding = "Hex", Data = "00FF" });
        var before = JsonSerializer.Serialize(configuration);
        BleClientProperties.IsVisible(configuration, "Value").ShouldBeTrue();
        configuration["useinputoperation"] = JsonSerializer.SerializeToElement(true);
        foreach (var name in new[] { "Operation", "ServiceId", "CharacteristicId", "WriteMode", "Value" })
            BleClientProperties.IsVisible(configuration, name).ShouldBeFalse();
        configuration.Remove("useinputoperation");
        JsonSerializer.Serialize(configuration).ShouldBe(before);
        configuration["connection"] = JsonSerializer.SerializeToElement("peripheral");
        BleClientProperties.IsVisible(configuration, "DeviceAddress").ShouldBeFalse();
        BleClientProperties.IsVisible(configuration, "AddressType").ShouldBeFalse();
        configuration["Type"] = JsonSerializer.SerializeToElement("ble.notifications");
        configuration["UseInputOperation"] = JsonSerializer.SerializeToElement(true);
        BleClientProperties.IsVisible(configuration, "ServiceId").ShouldBeTrue();
        BleClientProperties.IsVisible(configuration, "CharacteristicId").ShouldBeTrue();
    }

    [Fact]
    public void Inline_payload_edit_preserves_extensions_casing_and_undo_redo_without_requiring_complete_setup()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Workflows":{"main":{"client":{"Type":"ble.client","value":{"encoding":"Hex","data":"00FF","Extra":42},"Extra":{"Keep":true}}}}}""");
        graph.ResetHistory();
        var node = graph.Diagram.Nodes.OfType<FluxFlow.Studio.UI.Features.Designer.Canvas.FlowNodeModel>().Single();
        var before = graph.ToProjectJson();
        BleClientProperties.SetPayload(node.Configuration, new() { Encoding = BleValueEncoding.Base64, Data = "AP8=" });
        BleClientProperties.Payload(node.Configuration)!.Decode().ShouldBe(new byte[] { 0, 255 });
        node.Configuration["value"].GetProperty("Extra").GetInt32().ShouldBe(42);
        node.Configuration["value"].GetProperty("data").GetString().ShouldBe("AP8=");
        node.Configuration.ContainsKey("Value").ShouldBeFalse();
        graph.RecordEdit();
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public void Numeric_options_and_binder_compatible_strings_do_not_hide_or_rewrite_a_saved_write()
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
            {"Operation":1,"UseInputOperation":"false","TimeoutSeconds":"0020","AddressType":1,
             "ServiceId":"{3F63B38E-98DF-4A52-A9F8-76B803A934B2}","DeviceAddress":"aabbccddeeff",
             "CharacteristicId":"F50D7529-4E5D-4088-B88F-E236CD1E7F29",
             "Value":{"eNcoding":1,"dAta":"AP8=","extension":17}}
            """)!;
        var before = JsonSerializer.Serialize(config);
        BleClientProperties.IsVisible(config, "Value").ShouldBeTrue();
        BleClientProperties.Payload(config)!.Decode().ShouldBe(new byte[] { 0, 255 });
        new BleTargetSelection("", "AABBCCDDEEFF", BleAddressType.Random, Guid.Parse(config["ServiceId"].GetString()!),
            Guid.Parse(config["CharacteristicId"].GetString()!)).Apply(config);
        JsonSerializer.Serialize(config).ShouldBe(before);
        BleClientProperties.SetPayload(config, new() { Encoding = BleValueEncoding.Base64, Data = "" });
        JsonSerializer.Serialize(config).ShouldBe(before.Replace("AP8=", ""));
    }

    private const string Original = """
        {"Type":"ble.client","deviceaddress":"010203040506","ServiceId":"3f63b38e-98df-4a52-a9f8-76b803a934b2",
         "CharacteristicId":"f50d7529-4e5d-4088-b88f-e236cd1e7f29","Extra":{"Keep":true}}
        """;
    [Fact]
    public void Read_preview_validates_only_the_read_target_and_timeout()
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Original)!;
        BleClientProperties.Set(config, "TimeoutSeconds", "0021");
        BleClientProperties.Set(config, "AddressType", 1);
        BleClientProperties.Set(config, "Value", "unrelated invalid write payload");
        var before = JsonSerializer.Serialize(config);
        var preview = BleClientProperties.PreviewOptions(config);
        preview.TimeoutSeconds.ShouldBe(21);
        preview.AddressType.ShouldBe(BleAddressType.Random);
        preview.Address.ShouldBe(0x010203040506UL);
        preview.Operation.ShouldBe(BleOperationKind.Read);
        JsonSerializer.Serialize(config).ShouldBe(before);
        BleClientProperties.Set(config, "TimeoutSeconds", "999999999999999999999");
        Should.Throw<ArgumentException>(() => BleClientProperties.PreviewOptions(config));
    }

    [Fact]
    public async Task Canceled_late_results_cannot_replace_a_new_scan()
    {
        var backend = new DelayedExplorer();
        using var session = new BleClientDiscoverySession(backend);
        var first = session.ScanAsync(); var old = backend.Scans[0];
        session.Cancel(); backend.Token.IsCancellationRequested.ShouldBeTrue();
        var next = session.ScanAsync();
        old.SetResult([new("old", BleAddressType.Public, "old", -50)]);
        await first;
        session.Busy.ShouldBeTrue(); session.Devices.ShouldBeEmpty();
        backend.Scans[1].SetResult([new("new", BleAddressType.Public, "new", -40)]);
        await next;
        session.Devices.Single().Name.ShouldBe("new"); session.Busy.ShouldBeFalse();
    }
    [Fact]
    public async Task Dispose_rejects_late_discovery_and_duplicate_requests_do_not_start()
    {
        var backend = new DelayedExplorer();
        var session = new BleClientDiscoverySession(backend);
        var pending = session.ScanAsync();
        await session.ScanAsync(); backend.Scans.Count.ShouldBe(1);
        session.Dispose();
        backend.Scans[0].SetResult([new("old", BleAddressType.Public, "old", -50)]);
        await pending;
        session.Devices.ShouldBeEmpty(); backend.Token.IsCancellationRequested.ShouldBeTrue();
    }
    [Fact]
    public async Task Preview_is_detached_and_target_change_clears_it_without_mutating_data()
    {
        var backend = new DelayedExplorer();
        using var session = new BleClientDiscoverySession(backend);
        await session.ReadAsync(BleClientProperties.PreviewOptions(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Original)!));
        session.Value.ShouldBe(new byte[] { 0, 255, 42 });
        backend.Bytes[0] = 7;
        session.Value![0].ShouldBe((byte)0);
        session.ClearTarget(); session.Value.ShouldBeNull(); session.Services.ShouldBeEmpty();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_project_rejects_pending_scan_results_and_errors(bool fail)
    {
        var backend = new DelayedExplorer();
        using var session = new BleClientDiscoverySession(backend);
        var current = true;
        var pending = session.ScanAsync(() => current);
        current = false;
        if (fail) backend.Scans[0].SetException(new InvalidOperationException("Old device error"));
        else backend.Scans[0].SetResult([new("old", BleAddressType.Public, "old", -50)]);
        await pending;
        session.Devices.ShouldBeEmpty();
        session.Error.ShouldBeNull();
        session.Busy.ShouldBeFalse();
        session.Status.ShouldContain("discarded");
    }

    private sealed class DelayedExplorer : IBleExplorer
    {
        public readonly List<TaskCompletionSource<BleDeviceInfo[]>> Scans = [];
        public CancellationToken Token;
        public byte[] Bytes = [0, 255, 42];
        public Task<BleDeviceInfo[]> ScanAsync(CancellationToken cancellation)
        {
            Token = cancellation;
            var source = new TaskCompletionSource<BleDeviceInfo[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Scans.Add(source); return source.Task;
        }
        public Task<BleServiceInfo[]> DiscoverAsync(BleClientOptions options, CancellationToken cancellation) => Task.FromResult<BleServiceInfo[]>([]);
        public Task<byte[]> ReadAsync(BleClientOptions options, CancellationToken cancellation) => Task.FromResult(Bytes);
    }
}
