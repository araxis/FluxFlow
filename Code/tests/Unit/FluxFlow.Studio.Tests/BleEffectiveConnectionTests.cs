using FluxFlow.Environments;
using FluxFlow.Authoring;
using FluxFlow.Plugins.Bluetooth.Design;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Connections.Effective;

using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class BleEffectiveConnectionTests
{
    private const string Project = """{"Resources":{"device":{"Type":"ble.peripheral","eNdPoInT":"ble://112233445566/public","Extra":42},"radio":{"Type":"ble.adapter","Endpoint":"ble-adapter://default"}},"Workflows":{"main":{}}}""";

    private static EffectiveConnectionSettings Resolve(ConfigurationResource connection, EnvironmentDefinition env, IReadOnlyList<ConfigurationResource> connections)
    {
        var type = BleInfrastructure.Services.Single(service => service.Type == connection.Type);
        var schema = connection.Type == BluetoothDesign.Adapter.Type ? BluetoothDesign.Adapter : BluetoothDesign.Peripheral;
        var description = new ConnectionDescription(schema.Type, schema.Title, schema.EndpointProperty, () => new())
            { Preview = new BluetoothDesign().Connection(schema).Preview };
        return EffectiveConnectionSettings.Resolve(connection, env, connections, "Endpoint", description, new EndpointEnvironmentAdapter(type)).Single();
    }

    [Theory]
    [InlineData("device", "ble://112233445566/not-an-address-type")]
    [InlineData("radio", "ble-adapter://secondary")]
    public void Unsupported_saved_identity_is_not_advertised_as_the_next_run_target(string name, string endpoint)
    {
        var graph = StudioTestServices.CreateGraph(); graph.LoadJson(Project);
        var connection = graph.Connections.Single(item => item.Name == name);
        ResourceJson.Set(connection.Settings, "Endpoint", endpoint);
        var result = Resolve(connection, graph.GetEnvironmentDefinition(), graph.Connections);
        result.Notice.ShouldNotBeNull(); result.Fields.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("removed")]
    [InlineData("host")]
    public void Explicit_unavailable_binding_never_falls_back_to_the_saved_peripheral_for_discovery(string? target)
    {
        var graph = StudioTestServices.CreateGraph(); graph.LoadJson(Project);
        var env = graph.GetEnvironmentDefinition();
        env.Topology.Nodes.AddRange([new() { Kind = EnvironmentNodeKind.Host, Id = "host", LocalHost = true },
            new() { Kind = EnvironmentNodeKind.Application, Name = "App", Bindings = new() { ["Resources:device"] = target } }]);
        var connection = graph.Connections.Single(item => item.Name == "device");
        Resolve(connection, env, graph.Connections).Fields.ShouldBeEmpty();
        Should.Throw<ArgumentException>(() => BleDiscoveryAddress.Resolve(DiscoveryEndpoints.Resolve(graph.Connections, env, "device", "main", "ble.peripheral"))).Message.ShouldContain("unavailable");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explicit_and_implicit_adapter_placements_must_share_the_application_host(bool explicitBinding)
    {
        var graph = StudioTestServices.CreateGraph(); graph.LoadJson(Project);
        var env = graph.GetEnvironmentDefinition(); var connection = graph.Connections.Single(item => item.Name == "radio");
        var app = new EnvironmentNode { Id = "app", Kind = EnvironmentNodeKind.Application, Host = "local" };
        var radio = new EnvironmentNode { Id = "radio", Kind = EnvironmentNodeKind.Service, Host = "remote",
            Resource = explicitBinding ? null : connection.Path,
            ExistingService = explicitBinding ? new() { Type = connection.Type, Endpoint = "ble-adapter://default" } : null };
        env.Topology.Nodes.AddRange([new() { Kind = EnvironmentNodeKind.Host, Id = "local", LocalHost = true },
            new() { Kind = EnvironmentNodeKind.Host, Id = "remote" }, app, radio]);
        if (explicitBinding) app.Bindings[connection.Path] = radio.Id;
        EffectiveConnectionSettings View() => Resolve(connection, env, graph.Connections);
        View().Notice!.ShouldContain("execution host"); View().Fields.ShouldBeEmpty();
        radio.Host = "local"; View().Notice.ShouldBeNull(); View().Fields.Single().Value.ShouldStartWith("ble-adapter://default");
    }

    [Fact]
    public void Instance_summary_and_run_request_match_after_binding_removal_undo_and_reopen()
    {
        var graph = StudioTestServices.CreateGraph(); graph.LoadJson(Project); graph.ResetHistory();
        var config = graph.ExportConfiguration(); var topology = graph.GetEnvironmentDefinition().Topology;
        var app = new EnvironmentNode { Kind = EnvironmentNodeKind.Application, Id = "app", Name = "App", Host = "local",
            Bindings = new() { ["Resources:device"] = "sensor" } };
        topology.Nodes.AddRange([new() { Kind = EnvironmentNodeKind.Host, Id = "local", Name = "Local", LocalHost = true }, app,
            new() { Kind = EnvironmentNodeKind.Service, Id = "sensor", Name = "Sensor", Host = "local",
                ExistingService = new() { Type = "ble.peripheral", Endpoint = "ble://223344556677/random" } }]);
        graph.SetTopology(topology);
        void Verify(bool bound)
        {
            var conn = graph.Connections.Single(item => item.Name == "device");
            var env = graph.GetEnvironmentDefinition(); var run = graph.CreateEnvironmentRunRequest().Instances.Single();
            var effective = Resolve(conn, env, graph.Connections);
            var endpoint = bound ? run.ExistingServices[conn.Path].Endpoint : ResourceJson.Text(conn.Settings, "Endpoint");
            run.ExistingServices.ContainsKey(conn.Path).ShouldBe(bound); effective.Fields.Single().Value.ShouldBe(endpoint);
            effective.Source.ShouldBe(bound ? "Bound service · Sensor" : "Manual connection");
            var discovery = BleDiscoveryAddress.Resolve(DiscoveryEndpoints.Resolve(graph.Connections, env, "device", "main", "ble.peripheral"));
            discovery.Address.ShouldBe(bound ? 0x223344556677UL : 0x112233445566UL);
            graph.ExportConfiguration().ShouldBe(config);
        }
        Verify(true); graph.LoadProject(graph.ToProjectJson()); graph.ResetHistory(); Verify(true);
        topology = graph.GetEnvironmentDefinition().Topology; topology.Nodes.Single(node => node.Id == "app").Bindings.Clear();
        graph.SetTopology(topology); Verify(false); graph.Undo(); Verify(true); graph.Redo(); Verify(false);
    }
}
