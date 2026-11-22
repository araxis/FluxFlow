using System.Text.Json;
using FluxFlow.Environments;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class MqttServiceSettingsTests
{
    [Fact]
    public void Mqtt_host_settings_survive_placement_bindings_history_and_reopen_without_changing_configuration()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://manual:1883","ClientId":"client","Username":"manual"}},"Workflows":{"main":{}}}""");
        var original = graph.ExportConfiguration();
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var source = new InfrastructureHost { Services = [new() { Endpoint = "wss://remote:8443/mqtt", Mqtt = new()
        { Username = "service", Password = "trial-password", TrustedCertificates = ["root.pem"],
            ClientCertificate = new() { Path = "client.pem", PrivateKeyPath = "client-key.pem", Password = "key-password" } } }] };
        var sourceJson = JsonSerializer.Serialize(source, EnvironmentJsonContext.Default.InfrastructureHost);
        source = JsonSerializer.Deserialize(sourceJson, EnvironmentJsonContext.Default.InfrastructureHost)!;
        var topology = graph.GetEnvironmentDefinition().Topology;
        var host = topology.PlaceHost(source, 100, 100);
        var service = topology.Nodes.Single(node => node.ExistingService is not null);
        topology.Nodes.Add(new() { Kind = EnvironmentNodeKind.Application, Name = "App", Host = host,
            Bindings = new() { ["Resources:broker"] = service.Id } });
        graph.SetTopology(topology);
        var saved = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(saved); reopened.ResetHistory();
        var bound = reopened.CreateEnvironmentRunRequest().Instances.Single().ExistingServices["Resources:broker"];
        bound.Mqtt!.Username.ShouldBe("service"); bound.Mqtt.Password.ShouldBe("trial-password");
        bound.Mqtt.TrustedCertificates.Single().ShouldBe("root.pem");
        bound.Endpoint.ShouldBe("wss://remote:8443/mqtt");
        bound.Mqtt.ClientCertificate!.PrivateKeyPath.ShouldBe("client-key.pem");
        bound.Mqtt.ClientCertificate.Password.ShouldBe("key-password");
        reopened.ExportConfiguration().ShouldBe(original);
        var changed = reopened.GetEnvironmentDefinition().Topology;
        var index = changed.Nodes.FindIndex(node => node.Id == service.Id);
        changed.Nodes[index] = changed.Nodes[index] with { ExistingService = bound with { Mqtt = bound.Mqtt with { Username = "changed" } } };
        Should.Throw<ArgumentException>(() => reopened.SetTopology(changed));
        reopened.ToProjectJson().ShouldBe(saved);
        var moved = reopened.GetEnvironmentDefinition().Topology;
        moved.Nodes.Single(node => node.Id == service.Id).X += 20;
        reopened.SetTopology(moved);
        reopened.CreateEnvironmentRunRequest().Instances.Single().ExistingServices["Resources:broker"].Mqtt!.ClientCertificate.ShouldBe(bound.Mqtt.ClientCertificate);
        reopened.Undo(); reopened.ToProjectJson().ShouldBe(saved);
        reopened.Redo(); reopened.ExportConfiguration().ShouldBe(original);
        var unbound = reopened.GetEnvironmentDefinition().Topology;
        unbound.Nodes.Single(node => node.Kind == EnvironmentNodeKind.Application).Bindings.Remove("Resources:broker").ShouldBeTrue();
        reopened.SetTopology(unbound);
        reopened.CreateEnvironmentRunRequest().Instances.Single().ExistingServices.ContainsKey("Resources:broker").ShouldBeFalse();
        reopened.ExportConfiguration().ShouldBe(original);
        reopened.Undo();
        reopened.CreateEnvironmentRunRequest().Instances.Single().ExistingServices["Resources:broker"].Mqtt!.Password.ShouldBe(bound.Mqtt.Password);
        reopened.Redo();
        reopened.CreateEnvironmentRunRequest().Instances.Single().ExistingServices.ContainsKey("Resources:broker").ShouldBeFalse();
        JsonSerializer.Serialize(source, EnvironmentJsonContext.Default.InfrastructureHost).ShouldBe(sourceJson);
    }
}
