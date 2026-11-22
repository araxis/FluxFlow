using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Connections.Effective;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class EffectiveConnectionSettingsTests
{
    internal const string Configuration = """
        {"Resources":{"broker":{"Type":"mqtt.client","Host":"manual.example","Tls":true,"ClientId":"client",
        "Username":"manual-user","Password":"manual-secret","ClientCertificate":{"Path":"manual.pfx","Password":"key-secret"},
        "TrustedCertificates":["manual-ca.pem"]}},"Workflows":{"main":{}}}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bound_service_sources_match_runtime_overlay_and_leave_manual_instance_untouched(bool ownSecurity)
    {
        var graph = StudioTestServices.CreateGraph(); graph.LoadJson(Configuration); graph.ResetHistory();
        var before = graph.ToProjectJson();
        var definition = graph.GetEnvironmentDefinition();
        var service = new InfrastructureService { Endpoint = "wss://remote:8443/mqtt?token=hidden-secret", Mqtt = ownSecurity ? new()
            { Username = "service-user", Password = "service-secret", TrustedCertificates = ["service-ca.pem"] } : null };
        definition.Topology.Nodes.AddRange([
            new() { Id = "host", Kind = EnvironmentNodeKind.Host, Name = "Remote host" },
            new() { Id = "service", Kind = EnvironmentNodeKind.Service, Name = "Secure broker", Host = "host", ExistingService = service },
            new() { Id = "app", Kind = EnvironmentNodeKind.Application, Name = "Bound app", Bindings = new() { ["resources:BROKER"] = "service" } },
            new() { Id = "manual", Kind = EnvironmentNodeKind.Application, Name = "Manual app" }
        ]);
        var views = EffectiveConnectionSettings.Resolve(graph.Connections.Single(), definition, graph.Connections, "Address", StudioTestServices.MqttConnection(), new FluxFlow.Environments.MqttEnvironmentAdapter());
        views.Count.ShouldBe(2);
        var bound = views[0]; bound.Source.ShouldBe("Bound service · Secure broker"); bound.Host.ShouldBe("Remote host");
        bound.Fields.Single(field => field.Name == "Endpoint").Value.ShouldBe("wss://remote:8443/mqtt (query hidden)");
        bound.Fields.Single(field => field.Name == "TLS").ShouldBe(new("TLS", "Enabled", "Bound service"));
        bound.Fields.Single(field => field.Name == "Username").Value.ShouldBe(ownSecurity ? "service-user" : "manual-user");
        bound.Fields.Single(field => field.Name == "Password").Source.ShouldBe(ownSecurity ? "Bound service" : "Manual connection · broker");
        bound.Fields.Single(field => field.Name == "Client certificate").Value.ShouldBe(ownSecurity ? "Not set" : "manual.pfx");
        views[1].Source.ShouldBe("Manual connection");
        views[1].Fields.Single(field => field.Name == "Endpoint").Value.ShouldBe("mqtts://manual.example:8883/");
        string.Join(" ", views.SelectMany(view => view.Fields).Select(field => field.Value)).ShouldNotContain("secret");
        graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, 8883)]
    [InlineData(9983, 9983)]
    public void Legacy_host_and_tls_agree_in_topology_and_connection_preview(int? configuredPort, int expectedPort)
    {
        var settings = new System.Text.Json.Nodes.JsonObject { ["Host"] = "broker", ["Tls"] = "true" };
        if (configuredPort.HasValue) settings["Port"] = configuredPort.Value;
        var address = new MqttEnvironmentAdapter().Endpoint(settings);
        var preview = StudioTestServices.MqttConnection().Preview!(settings);
        new Uri(address!).ShouldBe(new Uri($"mqtts://broker:{expectedPort}"));
        new Uri(preview.Endpoint!).ShouldBe(new Uri(address!));
    }

    [Fact]
    public void Plaintext_binding_does_not_report_inherited_certificates_as_effective()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Configuration);
        var before = graph.ToProjectJson();
        graph.ResetHistory();
        var connection = graph.Connections.Single();
        var environment = graph.GetEnvironmentDefinition();
        environment.Topology.Nodes.AddRange([
            new() { Id = "service", Kind = EnvironmentNodeKind.Service, Name = "Plain broker", ExistingService = new() { Endpoint = "mqtt://plain:1883" } },
            new() { Kind = EnvironmentNodeKind.Application, Name = "App", Bindings = new() { [connection.Path] = "service" } }
        ]);
        var result = EffectiveConnectionSettings.Resolve(connection, environment, graph.Connections, "Address",
            StudioTestServices.MqttConnection(), new MqttEnvironmentAdapter()).Single();
        result.Notice.ShouldNotBeNull();
        result.Fields.ShouldBeEmpty();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void An_explicit_empty_override_clears_manual_credentials_and_missing_plugin_does_not_guess()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Configuration);
        var connection = graph.Connections.Single();
        var definition = graph.GetEnvironmentDefinition();
        var service = new InfrastructureService { Endpoint = "mqtts://remote:8883", Mqtt = new() };
        definition.Topology.Nodes.AddRange([
            new() { Id = "service", Kind = EnvironmentNodeKind.Service, Name = "Broker", ExistingService = service },
            new() { Kind = EnvironmentNodeKind.Application, Name = "App", Bindings = new() { [connection.Path] = "service" } }
        ]);
        var installed = EffectiveConnectionSettings.Resolve(connection, definition, graph.Connections, "Address",
            StudioTestServices.MqttConnection(), new MqttEnvironmentAdapter()).Single();
        installed.Fields.Single(f => f.Name == "Password").ShouldBe(new("Password", "Not set", "Bound service"));
        installed.Fields.Single(f => f.Name == "Revocation").ShouldBe(new("Revocation", "Online", "Bound service"));
        var absent = EffectiveConnectionSettings.Resolve(connection, definition, graph.Connections, "Address").Single();
        absent.Fields.ShouldBeEmpty();
        absent.Notice.ShouldNotBeNull();
    }

    [Fact]
    public void Missing_binding_and_container_setup_do_not_claim_manual_settings_are_effective()
    {
        var graph = StudioTestServices.CreateGraph(); graph.LoadJson(Configuration);
        var definition = graph.GetEnvironmentDefinition();
        definition.Topology.Nodes.Add(new() { Kind = EnvironmentNodeKind.Application, Name = "App", Bindings = new() { ["Resources:broker"] = "removed" } });
        var missing = EffectiveConnectionSettings.Resolve(graph.Connections.Single(), definition, graph.Connections, "Address", StudioTestServices.MqttConnection(), new FluxFlow.Environments.MqttEnvironmentAdapter()).Single();
        missing.Source.ShouldBe("Unavailable binding"); missing.Fields.ShouldBeEmpty();
        definition.Topology.Nodes.Clear();
        definition.Setup.Resources["Resources:broker"] = new() { Provider = "mosquitto" };
        var managed = EffectiveConnectionSettings.Resolve(graph.Connections.Single(), definition, graph.Connections, "Address", StudioTestServices.MqttConnection(), new FluxFlow.Environments.MqttEnvironmentAdapter()).Single();
        managed.Notice!.ShouldContain("assigned at startup"); managed.Fields.ShouldBeEmpty();
    }
}
