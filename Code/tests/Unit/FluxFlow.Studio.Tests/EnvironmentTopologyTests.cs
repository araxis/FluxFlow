using System.Text.Json.Nodes;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Environments;
using FluxFlow.Studio.UI.Features.Environments.Topology;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class EnvironmentTopologyTests
{
    private static readonly IResourceEnvironmentAdapter[] ServiceAdapters = [new HttpEnvironmentAdapter(), new MqttEnvironmentAdapter(), new RabbitMqEnvironmentAdapter(), new NatsEnvironmentAdapter()];

    [Theory]
    [InlineData("mqtt.client", "Address", "123")]
    [InlineData("mqtt.client", "Host", "{}")]
    [InlineData("http.client", "BaseAddress", "[]")]
    [InlineData("amqp.connection", "Endpoint", "[]")]
    public void Malformed_endpoint_settings_remain_inspectable(string type, string property, string value)
    {
        var resource = new FluxFlow.Authoring.ConfigurationResource("service", null,
            new JsonObject { ["Type"] = type, [property] = JsonNode.Parse(value) });
        var original = resource.Settings.ToJsonString();
        var result = TopologyEndpoint.Resolve(new(), new() { Kind = EnvironmentNodeKind.Service }, resource, null, ServiceAdapters);
        result.Value.ShouldBe("Invalid endpoint");
        result.Warning.ShouldNotBeNull();
        resource.Settings.ToJsonString().ShouldBe(original);
    }

    [Theory]
    [InlineData("mqtt://localhost:1883", true)]
    [InlineData("mqtt://LOCALHOST.:1883", true)]
    [InlineData("mqtt://127.0.0.1:1883", true)]
    [InlineData("mqtt://127.1.2.3:1883", true)]
    [InlineData("mqtt://[::1]:1883", true)]
    [InlineData("mqtt://[::ffff:127.0.0.1]:1883", true)]
    [InlineData("mqtt://broker.internal:1883", false)]
    [InlineData("mqtt://localhost.example.com:1883", false)]
    [InlineData("invalid endpoint", false)]
    public void Remote_service_endpoint_warning_is_read_only_and_detects_loopback(string address, bool warn)
    {
        var resource = new FluxFlow.Authoring.ConfigurationResource("broker", null,
            new JsonObject { ["Type"] = "mqtt.client", ["Address"] = address });
        var host = new EnvironmentNode { Id = "host", Kind = EnvironmentNodeKind.Host, Name = "Remote" };
        var service = new EnvironmentNode { Id = "broker", Kind = EnvironmentNodeKind.Service, Resource = resource.Path, Host = host.Id };
        var definition = new EnvironmentDefinition { Topology = new() { Nodes = [host, service] } };
        var original = resource.Settings.ToJsonString();
        var result = TopologyEndpoint.Resolve(definition, service, resource, null, ServiceAdapters);
        result.Value.ShouldBe(address == "invalid endpoint" ? "Invalid endpoint" : address);
        (result.Warning is not null).ShouldBe(warn);
        service.Host = null;
        TopologyEndpoint.Resolve(definition, service, resource, null, ServiceAdapters).Warning.ShouldBeNull();
        definition.Topology.Nodes[0] = host with { LocalHost = true };
        service.Host = host.Id;
        TopologyEndpoint.Resolve(definition, service, resource, null, ServiceAdapters).Warning.ShouldBeNull();
        resource.Settings.ToJsonString().ShouldBe(original);
    }

    [Theory]
    [InlineData("http.client", "BaseAddress", "https://localhost/v1/readings", "wiremock", "http://127.0.0.1:45678/", "http://127.0.0.1:45678/v1/readings")]
    [InlineData("amqp.connection", "Endpoint", "amqp://localhost:5672", "rabbitmq", "amqp://127.0.0.1:45678", "amqp://127.0.0.1:45678/")]
    [InlineData("nats.connection", "Endpoint", "nats://localhost:4222", "nats", "nats://127.0.0.1:45678", "nats://127.0.0.1:45678/")]
    public void Endpoint_display_reuses_adapters_and_does_not_show_container_configuration_as_live(
        string type, string property, string address, string provider, string endpoint, string expected)
    {
        var resource = new FluxFlow.Authoring.ConfigurationResource("api", "main",
            new JsonObject { ["Type"] = type, [property] = address });
        var service = new EnvironmentNode { Id = "api", Kind = EnvironmentNodeKind.Service, Resource = resource.Path, Host = "remote" };
        var definition = new EnvironmentDefinition { Topology = new() { Nodes = [new() { Id = "remote", Kind = EnvironmentNodeKind.Host }, service] } };
        TopologyEndpoint.Resolve(definition, service, resource, null, ServiceAdapters).Value.ShouldBe(address);
        TopologyEndpoint.Resolve(definition, service, resource, null, ServiceAdapters).Warning.ShouldNotBeNull();
        definition.Setup.Resources[resource.Path] = new() { Provider = provider };
        var pending = TopologyEndpoint.Resolve(definition, service, resource, null, ServiceAdapters);
        pending.Value.ShouldBe("Assigned at startup");
        pending.Warning.ShouldBeNull();
        var original = resource.Settings.ToJsonString();
        var live = TopologyEndpoint.Resolve(definition, service, resource,
            new(resource.Path, provider, endpoint, "container"), ServiceAdapters);
        live.Value.ShouldBe(expected);
        live.Warning.ShouldBeNull();
        resource.Settings.ToJsonString().ShouldBe(original);
        TopologyEndpoint.Resolve(definition, service, null, null).Value.ShouldBe("Connection unavailable");
        var mqtt = new FluxFlow.Authoring.ConfigurationResource("broker", null,
            new JsonObject { ["Type"] = "mqtt.client", ["Host"] = "localhost", ["Port"] = 1884 });
        TopologyEndpoint.Resolve(definition, service, mqtt, null, ServiceAdapters).Value.ShouldBe("mqtt://localhost:1884");
        TopologyEndpoint.Resolve(definition, service, mqtt, null, ServiceAdapters).Warning.ShouldNotBeNull();
    }

    [Fact]
    public async Task Effective_container_endpoints_are_only_available_for_the_current_running_snapshot()
    {
        var graph = StudioTestServices.CreateGraph();
        var original = graph.ToProjectJson();
        graph.ResetHistory();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        await using var session = new StudioRunSession(runner);
        var start = session.StartAsync(graph);
        session.CurrentServices(graph).ShouldBeEmpty();
        var service = new PreparedService("Resources:api", "wiremock", "http://127.0.0.1:45000/", "container");
        runner.Release.SetResult(new(runner.Id, "Running", [service]));
        await start;
        session.CurrentServices(graph).ShouldBe([service]);
        graph.ToProjectJson().ShouldBe(original);
        graph.CanUndo.ShouldBeFalse();
        await session.StopAsync();
        session.CurrentServices(graph).ShouldBeEmpty();
        start = session.StartAsync(graph);
        session.CurrentServices(graph).ShouldBeEmpty();
        runner.Release.SetResult(new(runner.Id, "Running", [service]));
        await start;
        graph.AddEnvironment("Other");
        session.CurrentServices(graph).ShouldBeEmpty();
        graph.Undo();
        session.CurrentServices(graph).ShouldBe([service]);
        graph.ToProjectJson().ShouldBe(original);
        graph.Redo();
        graph.Environments.ShouldContain(environment => environment.Name == "Other");
        await session.StopAsync();
    }

    [Fact]
    public async Task Topology_run_scope_survives_switching_layout_and_stop_but_not_replaced_projects_or_changed_setup()
    {
        var graph = StudioTestServices.CreateGraph();
        var initial = new EnvironmentTopology { Nodes = [new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application }] };
        var hostId = initial.PlaceHost(new(), 200, 100);
        graph.SetTopology(initial);
        var first = graph.ActiveEnvironmentId;
        graph.AddEnvironment("Other");
        var second = graph.ActiveEnvironmentId;
        graph.SelectEnvironment(first);
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        await using var session = new StudioRunSession(runner);
        var start = session.StartAsync(graph);
        session.CurrentTopologyStatus(graph)!.State.ShouldBe("Preparing");
        runner.Release.SetResult(new(runner.Id, "Failed", [], "Startup failure"));
        await start;
        session.ResolveApplicationTelemetry(graph, "app", runner.Id)!.InstanceId.ShouldBe("app");
        session.ResolveApplicationTelemetry(graph, "app", Guid.NewGuid()).ShouldBeNull();
        session.ResolveApplicationTelemetry(graph, "removed", runner.Id).ShouldBeNull();
        graph.SelectEnvironment(second);
        session.CurrentTopologyStatus(graph).ShouldBeNull();
        session.ResolveApplicationTelemetry(graph, "app", runner.Id).ShouldBeNull();
        graph.SelectEnvironment(first);
        session.CurrentTopologyStatus(graph)!.State.ShouldBe("Failed");
        graph.CanUndo.ShouldBeFalse();
        graph.ToProjectJson().ShouldBe(original);
        var topology = graph.GetEnvironmentDefinition().Topology;
        topology.Nodes[0].X += 20;
        topology.Nodes[0].Color = "#123456";
        topology.Nodes.Single(node => node.Id == hostId).PlacementScale = .5;
        graph.SetTopology(topology);
        session.CurrentTopologyStatus(graph).ShouldNotBeNull();
        graph.Undo();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 120 });
        session.CurrentTopologyStatus(graph).ShouldBeNull();
        session.ResolveApplicationTelemetry(graph, "app", runner.Id).ShouldBeNull();
        graph.Undo();
        await session.StopAsync();
        session.CurrentTopologyStatus(graph)!.State.ShouldBe("Stopped");
        session.ResolveApplicationTelemetry(graph, "app", runner.Id).ShouldNotBeNull();
        session.Failure.ShouldBe("Startup failure");
        graph.ToProjectJson().ShouldBe(original);
        graph.LoadProject(original);
        session.CurrentTopologyStatus(graph).ShouldBeNull();
        session.ResolveApplicationTelemetry(graph, "app", runner.Id).ShouldBeNull();
    }

    [Fact]
    public void Topology_status_does_not_invent_service_health_or_failure_attribution()
    {
        var service = new EnvironmentNode { Id = "api", Kind = EnvironmentNodeKind.Service, Resource = "Resources:api" };
        var definition = new EnvironmentDefinition();
        var running = new EnvironmentRunStatus(Guid.NewGuid(), "Running", []);
        TopologyRunState.Resolve(service, definition, running)!.Label.ShouldBe("External");
        definition.Setup.Resources[service.Resource] = new() { Provider = "wiremock" };
        TopologyRunState.Resolve(service, definition, running)!.Label.ShouldBe("Not reported");
        var prepared = running with { Services = [new(service.Resource, "wiremock", "http://localhost:12345/", "container")] };
        TopologyRunState.Resolve(service, definition, prepared)!.Label.ShouldBe("Running");
        TopologyRunState.Resolve(service, definition, prepared with { State = "Failed" })!.Label.ShouldBe("Run failed");
        TopologyRunState.Resolve(service, definition, prepared with { State = "Stopped", Services = [] })!.Label.ShouldBe("Stopped");
        TopologyRunState.Resolve(service with { Kind = EnvironmentNodeKind.Host }, definition, running).ShouldBeNull();
        TopologyRunState.Resolve(service, definition, null).ShouldBeNull();
    }

    [Fact]
    public void Existing_topology_without_bindings_preserves_configured_connections()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.SetTopology(new() { Nodes = [new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application }] });
        var project = JsonNode.Parse(graph.ToProjectJson())!;
        foreach (var node in project["Environments"]![0]!["Topology"]!["Nodes"]!.AsArray())
            node!.AsObject().Remove("Bindings");
        graph.LoadProject(project.ToJsonString());
        graph.GetEnvironmentDefinition().Topology.Nodes.Single().Bindings.ShouldBeEmpty();
        graph.CreateEnvironmentRunRequest().Instances.Single().Bindings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Connection_links_validate_capture_and_undo_without_rewriting_configuration()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"},"other":{"Type":"mqtt.client","Address":"mqtt://other:1883"},"api":{"Type":"http.client","BaseAddress":"http://original/"}},"Workflows":{"main":{}}}""");
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application },
            new() { Id = "mqtt", Name = "Other broker", Kind = EnvironmentNodeKind.Service, Resource = "Resources:other" },
            new() { Id = "http", Name = "API", Kind = EnvironmentNodeKind.Service, Resource = "Resources:api" }
        ] });
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        var diagram = ContainerDiagram(graph.GetEnvironmentDefinition().Topology);
        var app = diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Id == "app");
        var mqtt = diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Id == "mqtt");
        var http = diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.Definition.Id == "http");
        app.AddConnectionPort("Resources:broker", "mqtt.client", "broker · MQTT");
        mqtt.AddConnectionPort("Resources:other", "mqtt.client", "other · MQTT");
        http.AddConnectionPort("Resources:api", "http.client", "api · HTTP");
        var port = app.ConnectionPorts.Single();
        port.CanAttachTo(mqtt.ConnectionPorts.Single()).ShouldBeTrue();
        port.CanAttachTo(http.ConnectionPorts.Single()).ShouldBeFalse();
        port.CanAttachTo(port).ShouldBeFalse();
        var link = new LinkModel(new SinglePortAnchor(port), new SinglePortAnchor(mqtt.ConnectionPorts.Single()));
        diagram.Links.Add(link);
        graph.SetTopology(EnvironmentTopologyPage.CaptureTopology(diagram, connections: graph.Connections));
        graph.EnvironmentLimitation().ShouldBeNull();
        graph.CreateEnvironmentRunRequest().Instances.Single().Bindings["Resources:broker"].ShouldBe("Resources:other");
        var connected = graph.ToProjectJson();
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(connected);
        reopened.CreateEnvironmentRunRequest().Instances.Single().Bindings["Resources:broker"].ShouldBe("Resources:other");
        graph.Undo();
        graph.ToProjectJson().ShouldBe(original);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(connected);
        app.Definition.Bindings["Resources:broker"] = "mqtt";
        port.InitialTargetId = "mqtt";
        diagram.Links.Remove(link);
        graph.SetTopology(EnvironmentTopologyPage.CaptureTopology(diagram, connections: graph.Connections));
        graph.EnvironmentLimitation()!.ShouldContain("unbound");
        await using var session = new StudioRunSession(new EnvironmentProjectTests.DelayedRunner());
        await session.CheckAsync(graph);
        session.CheckResult!.Success.ShouldBeFalse();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(connected);
        var invalid = graph.GetEnvironmentDefinition().Topology;
        invalid.Nodes[0].Bindings["Resources:broker"] = "http";
        graph.SetTopology(invalid);
        graph.EnvironmentLimitation()!.ShouldContain("incompatible");
        invalid.Nodes.RemoveAll(node => node.Id == "http");
        graph.SetTopology(invalid);
        graph.EnvironmentLimitation()!.ShouldContain("unbound");
        graph.ExportConfiguration().ShouldBe(configuration);
    }

    [Theory]
    [InlineData("e")]
    [InlineData("s")]
    [InlineData("se")]
    public void Resizing_past_minimum_keeps_the_opposite_edge_fixed(string edge)
    {
        var node = new TopologyNodeModel(new() { Kind = EnvironmentNodeKind.Host, Name = "Host", X = 100, Y = 120, Width = 400, Height = 300 }, "", "");
        node.ResizeBounds(100, 120, edge.Contains('e') ? 90 : 500, edge.Contains('s') ? 110 : 420, edge);
        node.Position.X.ShouldBe(100);
        node.Position.Y.ShouldBe(120);
        node.Size!.Width.ShouldBe(edge.Contains('e') ? 240 : 400);
        node.Size.Height.ShouldBe(edge.Contains('s') ? 180 : 300);
    }

    [Fact]
    public void Topology_colors_roundtrip_reset_and_undo_without_changing_execution_configuration()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"}},"Workflows":{"main":{}}}""");
        var topology = graph.GetEnvironmentDefinition().Topology;
        var source = new InfrastructureHost { Services = [new() { Name = "Broker" }] }.CreateTopology();
        source.Nodes.Single(n => n.Kind == EnvironmentNodeKind.Service).Color = "#BeEf01";
        var host = topology.PlaceHost(new() { Topology = source }, 40, 80);
        var service = topology.Nodes.Single(n => n.ExistingService is not null);
        topology.Nodes.Add(new() { Id = "app", Kind = EnvironmentNodeKind.Application, Name = "Application", Host = host });
        topology.SetBinding("app", "Resources:broker", TopologyBinding.Explicit(service.Id), _ => "mqtt.client");
        graph.SetTopology(topology);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        topology.SetColor(host, "#123456").ShouldBeFalse();
        topology.SetColor(service.Id, "#123456").ShouldBeFalse();
        topology.SetColor(service.Id, null).ShouldBeFalse();
        service.Color.ShouldBe("#BeEf01");
        topology.SetColor("removed", "#123456").ShouldBeFalse();
        topology.SetColor("app", "#ab2345").ShouldBeTrue();
        topology.SetColor("app", "#AB2345").ShouldBeFalse();
        graph.SetTopology(topology);
        var colored = graph.ToProjectJson();
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(colored);
        var app = reopened.GetEnvironmentDefinition().Topology.Nodes.Single(n => n.Id == "app");
        app.Color.ShouldBe("#ab2345");
        app.Bindings["Resources:broker"].ShouldBe(service.Id);
        reopened.ToProjectJson().ShouldBe(colored);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(colored);
        topology.SetColor("app", null).ShouldBeTrue();
        topology.SetColor("app", null).ShouldBeFalse();
        graph.SetTopology(topology);
        graph.ToProjectJson().ShouldBe(before);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(colored);
        topology = graph.GetEnvironmentDefinition().Topology;
        Should.Throw<ArgumentException>(() => topology.SetColor("app", "red;display:none"));
        topology.Nodes.Single(n => n.Id == "app").Color.ShouldBe("#ab2345");
        topology.Nodes.Single(n => n.Id == "app").Color = "red;display:none";
        Should.Throw<ArgumentException>(() => graph.SetTopology(topology));
        graph.ToProjectJson().ShouldBe(colored);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(before);
        reopened.LoadProject(graph.ToProjectJson());
        reopened.ToProjectJson().ShouldBe(before);
        graph.ExportConfiguration().ShouldBe(configuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Containment_and_edge_resize_keep_children_and_saved_history_intact(bool savedContainment)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "host", Name = "Host", Kind = EnvironmentNodeKind.Host, Width = 800, Height = 700 },
            new() { Id = "net", Name = "Network", Kind = EnvironmentNodeKind.Network, Host = "host", X = 30, Y = 80, Width = 600, Height = 450, KeepInsideContainer = savedContainment },
            new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Host = "host", Networks = ["net"], X = 100, Y = 200, KeepInsideContainer = savedContainment },
            new() { Id = "app2", Name = "Application 2", Kind = EnvironmentNodeKind.Application }
        ] });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var diagram = ContainerDiagram(graph.GetEnvironmentDefinition().Topology);
        var nodes = diagram.Nodes.OfType<TopologyNodeModel>().ToArray();
        var app = nodes.Single(node => node.Definition.Id == "app");
        var network = nodes.Single(node => node.Definition.Id == "net");
        app.SetPosition(2000, 2000);
        TopologyContainers.Drop(app, nodes);
        app.Container.ShouldBe(network);
        app.Position.X.ShouldBe(network.Position.X + network.Size!.Width - app.Size!.Width - 24);
        app.Position.Y.ShouldBe(network.Position.Y + network.Size.Height - app.Size.Height - 24);
        var position = app.Position;
        network.ResizeBounds(-100, -100, 750, 650);
        network.Position.X.ShouldBe(24);
        network.Position.Y.ShouldBe(76);
        app.Position.ShouldBe(position);
        network.ResizeBounds(700, 600, 750, 650);
        network.Position.X.ShouldBeLessThanOrEqualTo(app.Position.X - 24);
        network.Position.Y.ShouldBeLessThanOrEqualTo(app.Position.Y - 76);
        graph.SetTopology(EnvironmentTopologyPage.CaptureTopology(diagram));
        var after = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        var request = graph.CreateEnvironmentRunRequest();
        request.Instances.Select(instance => instance.Id).ShouldBe(new[] { "app", "app2" });
        var restored = StudioTestServices.CreateGraph();
        restored.LoadProject(after);
        restored.GetEnvironmentDefinition().Topology.Nodes.Single(node => node.Id == "app").KeepInsideContainer.ShouldBe(savedContainment);
        diagram.Nodes.Remove(network);
        diagram.Nodes.Remove(nodes.Single(node => node.Definition.Id == "host"));
        EnvironmentTopologyPage.CaptureTopology(diagram).Validate();
    }

    [Fact]
    public void Host_member_can_be_dropped_into_its_network_then_stays_inside()
    {
        var diagram = ContainerDiagram(new() { Nodes =
        [
            new() { Id = "host", Name = "Host", Kind = EnvironmentNodeKind.Host, Width = 900, Height = 700 },
            new() { Id = "net", Name = "Network", Kind = EnvironmentNodeKind.Network, Host = "host", X = 300, Y = 100, Width = 500, Height = 450 },
            new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Host = "host", X = 30, Y = 100 }
        ] });
        var nodes = diagram.Nodes.OfType<TopologyNodeModel>().ToArray();
        var app = nodes.Single(node => node.Definition.Id == "app");
        var network = nodes.Single(node => node.Definition.Id == "net");
        app.SetPosition(340, 200);
        TopologyContainers.Drop(app, nodes);
        app.Container.ShouldBe(network);
        app.Definition.Host.ShouldBe("host");
        app.Definition.Networks.ShouldContain("net");
        app.SetPosition(-100, -100);
        TopologyContainers.Drop(app, nodes);
        app.Container.ShouldBe(network);
        app.Position.X.ShouldBe(324);
        app.Position.Y.ShouldBe(176);
        EnvironmentTopologyPage.CaptureTopology(diagram).Validate();
    }

    [Fact]
    public void Nested_containers_move_resize_and_roundtrip_as_one_history_edit()
    {
        var graph = StudioTestServices.CreateGraph();
        var configuration = graph.ExportConfiguration();
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "host", Name = "Computer", Kind = EnvironmentNodeKind.Host, X = 40, Y = 40, Width = 700, Height = 600, LocalHost = true },
            new() { Id = "net", Name = "LAN", Kind = EnvironmentNodeKind.Network, Host = "host", X = 72, Y = 120, Width = 600, Height = 450 },
            new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Host = "host", Networks = ["net"], X = 120, Y = 220 }
        ] });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var diagram = ContainerDiagram(graph.GetEnvironmentDefinition().Topology);
        var nodes = diagram.Nodes.OfType<TopologyNodeModel>().ToArray();
        var host = nodes.Single(node => node.Definition.Id == "host");
        var network = nodes.Single(node => node.Definition.Id == "net");
        var app = nodes.Single(node => node.Definition.Id == "app");
        network.Container.ShouldBe(host);
        app.Container.ShouldBe(network);
        host.SetPosition(90, 70);
        app.Position.X.ShouldBe(170);
        app.Position.Y.ShouldBe(250);
        network.Position.X.ShouldBe(122);
        network.Resize(240, 180);
        (network.Position.X + network.Size!.Width).ShouldBeGreaterThanOrEqualTo(app.Position.X + app.Size!.Width);
        graph.SetTopology(EnvironmentTopologyPage.CaptureTopology(diagram));
        var after = graph.ToProjectJson();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(after);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(after);
        reopened.GetEnvironmentDefinition().Topology.Nodes.Single(node => node.Id == "net").Width.ShouldBe(network.Size.Width);
        reopened.ExportConfiguration().ShouldBe(configuration);
        diagram.Nodes.Remove(host);
        diagram.Nodes.Remove(network);
        var remaining = EnvironmentTopologyPage.CaptureTopology(diagram);
        remaining.Validate();
        remaining.Nodes.Single().Host.ShouldBeNull();
        remaining.Nodes.Single().Networks.ShouldBeEmpty();
    }

    [Fact]
    public void Dropping_into_boundaries_preserves_other_network_memberships_and_prevents_escape()
    {
        var diagram = ContainerDiagram(new() { Nodes =
        [
            new() { Id = "host", Name = "Computer", Kind = EnvironmentNodeKind.Host, X = 0, Y = 0, Width = 800, Height = 700 },
            new() { Id = "net", Name = "LAN", Kind = EnvironmentNodeKind.Network, Host = "host", X = 30, Y = 80, Width = 600, Height = 450 },
            new() { Id = "other", Name = "Other LAN", Kind = EnvironmentNodeKind.Network, X = 1000, Y = 0, Width = 400, Height = 300 },
            new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Host = "host", Networks = ["other"], X = 1000, Y = 400 }
        ] });
        var nodes = diagram.Nodes.OfType<TopologyNodeModel>().ToArray();
        var app = nodes.Single(node => node.Definition.Id == "app");
        app.SetPosition(100, 200);
        TopologyContainers.Drop(app, nodes);
        app.Definition.Host.ShouldBe("host");
        app.Definition.Networks.ShouldBe(new[] { "net", "other" });
        app.Container!.Definition.Id.ShouldBe("net");
        app.SetPosition(700, 600);
        TopologyContainers.Drop(app, nodes);
        app.Definition.Host.ShouldBe("host");
        app.Definition.Networks.ShouldBe(new[] { "net", "other" });
        app.SetPosition(1800, 1000);
        TopologyContainers.Drop(app, nodes);
        app.Definition.Host.ShouldBe("host");
        app.Definition.Networks.ShouldBe(new[] { "net", "other" });
        app.Container!.Definition.Id.ShouldBe("net");
        app.Position.X.ShouldBeLessThanOrEqualTo(app.Container.Position.X + app.Container.Size!.Width - app.Size!.Width - 24);
        app.Position.Y.ShouldBeLessThanOrEqualTo(app.Container.Position.Y + app.Container.Size!.Height - app.Size!.Height - 24);
    }

    [Fact]
    public void Legacy_boundaries_are_framed_without_editing_saved_positions_or_dimensions()
    {
        var topology = new EnvironmentTopology { Nodes =
        [
            new() { Id = "host", Name = "Computer", Kind = EnvironmentNodeKind.Host, X = 400, Y = 100 },
            new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Host = "host", X = 100, Y = 200 }
        ] };
        var diagram = ContainerDiagram(topology);
        var saved = EnvironmentTopologyPage.CaptureTopology(diagram);
        saved.Nodes.Single(node => node.Id == "host").X.ShouldBe(400);
        saved.Nodes.Single(node => node.Id == "host").Width.ShouldBeNull();
        saved.Nodes.Single(node => node.Id == "app").X.ShouldBe(100);
        var host = diagram.Nodes.OfType<TopologyNodeModel>().Single(node => node.IsContainer);
        host.Position.X.ShouldBeLessThan(100);
        topology.Nodes[0].X.ShouldBe(400);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_boundary_dimensions_are_rejected_without_changing_history(double width)
    {
        var graph = StudioTestServices.CreateGraph();
        var before = graph.ToProjectJson();
        Should.Throw<ArgumentException>(() => graph.SetTopology(new() { Nodes =
            [new() { Name = "Host", Kind = EnvironmentNodeKind.Host, Width = width }] }));
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void Container_render_order_and_primary_network_do_not_create_a_selection_edit()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Host = "host", Networks = ["external", "net"], X = 100, Y = 200 },
            new() { Id = "host", Name = "Computer", Kind = EnvironmentNodeKind.Host },
            new() { Id = "net", Name = "LAN", Kind = EnvironmentNodeKind.Network, Host = "host" },
            new() { Id = "external", Name = "Other LAN", Kind = EnvironmentNodeKind.Network }
        ] });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var topology = graph.GetEnvironmentDefinition().Topology;
        var diagram = new BlazorDiagram();
        foreach (var definition in topology.Nodes.OrderBy(node => node.Kind == EnvironmentNodeKind.Host ? 0 : node.Kind == EnvironmentNodeKind.Network ? 1 : 2))
        {
            var node = new TopologyNodeModel(definition, "", "") { Sequence = topology.Nodes.IndexOf(definition) };
            diagram.Nodes.Add(node);
        }
        var nodes = diagram.Nodes.OfType<TopologyNodeModel>().ToArray();
        TopologyContainers.Arrange(nodes);
        var app = nodes.Single(node => node.Definition.Id == "app");
        graph.SetTopology(EnvironmentTopologyPage.CaptureTopology(diagram));
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    private static BlazorDiagram ContainerDiagram(EnvironmentTopology topology)
    {
        var diagram = new BlazorDiagram();
        foreach (var definition in topology.Nodes)
        {
            var node = new TopologyNodeModel(definition with { Networks = [.. definition.Networks] }, "", "");
            diagram.Nodes.Add(node);
        }
        TopologyContainers.Arrange(diagram.Nodes.OfType<TopologyNodeModel>().ToArray());
        return diagram;
    }

    [Fact]
    public void Undo_and_redo_preserve_case_insensitive_resource_lookup_for_remote_placement_checks()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://localhost:1883"}},"Workflows":{"main":{}}}""");
        graph.SetEnvironment(new() { Resources = new() { ["resources:BROKER"] = new() { Provider = "mosquitto" } } });
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "remote", Name = "Remote", Kind = EnvironmentNodeKind.Host },
            new() { Id = "broker", Name = "Broker", Kind = EnvironmentNodeKind.Service, Resource = "Resources:broker", Host = "remote" }
        ] });
        graph.ResetHistory();
        graph.EnvironmentLimitation().ShouldNotBeNull();
        graph.RenameEnvironment("Renamed");
        graph.Undo();
        graph.EnvironmentLimitation().ShouldNotBeNull();
        graph.Redo();
        graph.EnvironmentLimitation().ShouldNotBeNull();
    }

    [Fact]
    public void Unchanged_legacy_names_survive_edits_without_normalizing_the_project_or_losing_redo()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.AddEnvironment("Workshop");
        var workshopId = graph.ActiveEnvironmentId;
        var saved = JsonNode.Parse(graph.ToProjectJson())!.AsObject();
        // Earlier projects could contain names differing only in surrounding whitespace.
        saved["Environments"]![0]!["Name"] = "Lab";
        saved["Environments"]![1]!["Name"] = " Lab ";
        graph.LoadProject(saved.ToJsonString());
        graph.ResetHistory();
        graph.RenameEnvironment("Workshop");
        graph.Undo();
        foreach (var env in graph.Environments)
        {
            graph.SelectEnvironment(env.Id);
            var before = graph.ToProjectJson();
            var revision = graph.Revision;
            graph.RenameEnvironment(env.Name);
            Should.Throw<ArgumentException>(() => graph.AddEnvironment(" lAb "));
            graph.ToProjectJson().ShouldBe(before);
            graph.Revision.ShouldBe(revision);
            graph.CanUndo.ShouldBeFalse();
            graph.CanRedo.ShouldBeTrue();
        }
        graph.Redo();
        graph.ActiveEnvironmentId.ShouldBe(workshopId);
        graph.EnvironmentName.ShouldBe("Workshop");
    }

    [Fact]
    public void Environment_names_preserve_history_identity_bindings_and_placed_snapshots()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://localhost:1883"}},"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var local = graph.ActiveEnvironmentId;
        graph.AddEnvironment("  Lab  ");
        var lab = graph.ActiveEnvironmentId;
        lab.ShouldNotBe(local);
        graph.EnvironmentName.ShouldBe("Lab");
        graph.Undo();
        graph.ActiveEnvironmentId.ShouldBe(local);
        graph.Redo();
        graph.SelectEnvironment(lab);
        var topology = graph.GetEnvironmentDefinition().Topology;
        var host = topology.PlaceHost(new() { Name = "Rig", Services = [new() { Name = "Broker", Endpoint = "mqtt://rig:1883" }] }, 50, 80);
        var service = topology.Nodes.Single(node => node.ExistingService is not null);
        topology.Nodes.Add(new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Host = host });
        topology.SetBinding("app", "Resources:broker", TopologyBinding.Explicit(service.Id), _ => "mqtt.client");
        graph.SetTopology(topology);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        graph.RenameEnvironment("  LAB  ");
        graph.EnvironmentName.ShouldBe("LAB");
        var after = graph.ToProjectJson();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        var revision = graph.Revision;
        Should.Throw<ArgumentException>(() => graph.AddEnvironment(" lab "));
        Should.Throw<ArgumentException>(() => graph.RenameEnvironment(" LOCAL DEVELOPMENT "));
        Should.Throw<ArgumentException>(() => graph.AddEnvironment(" "));
        Should.Throw<ArgumentException>(() => graph.RenameEnvironment(" "));
        graph.RenameEnvironment(" Lab ");
        graph.Revision.ShouldBe(revision);
        graph.CanUndo.ShouldBeFalse();
        graph.CanRedo.ShouldBeTrue();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(after);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(after);
        reopened.ActiveEnvironmentId.ShouldBe(lab);
        reopened.EnvironmentName.ShouldBe("LAB");
        var restored = reopened.GetEnvironmentDefinition().Topology;
        restored.Nodes.Single(n => n.Id == "app").Bindings["Resources:broker"].ShouldBe(service.Id);
        restored.Nodes.Single(n => n.Id == service.Id).InfrastructureGroup.ShouldBe(host);
        reopened.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public void Legacy_setup_migrates_and_named_environments_keep_independent_snapshots()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 91, Resources = new()
        {
            ["Resources:api"] = new() { Provider = "wiremock", Mappings = "[]", Files = new() { ["/example.txt"] = "Keep" } }
        } });
        var legacy = JsonNode.Parse(graph.ToProjectJson())!.AsObject();
        legacy.Remove("Environments");
        legacy.Remove("ActiveEnvironment");
        graph.LoadProject(legacy.ToJsonString());
        graph.Environments.Count.ShouldBe(1);
        graph.EnvironmentName.ShouldBe("Local development");
        var local = graph.ActiveEnvironmentId;
        var configuration = graph.ExportConfiguration();
        graph.AddEnvironment("Integration test");
        var integration = graph.ActiveEnvironmentId;
        graph.GetEnvironment().Resources.ShouldBeEmpty();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 120 });
        graph.SelectEnvironment(local);
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(91);
        graph.GetEnvironment().Resources["resources:API"].Files["/example.txt"].ShouldBe("Keep");
        graph.SelectEnvironment(integration);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(graph.ToProjectJson());
        reopened.ActiveEnvironmentId.ShouldBe(integration);
        reopened.GetEnvironment().StartupTimeoutSeconds.ShouldBe(120);
        reopened.SelectEnvironment(local);
        reopened.GetEnvironment().Resources["resources:API"].Mappings.ShouldBe("[]");
        reopened.ExportConfiguration().ShouldBe(configuration);
    }

    [Fact]
    public void Topology_edits_are_isolated_undoable_and_do_not_change_connection_setup()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://localhost:1883"}},"Workflows":{"main":{}}}""");
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = new() { Provider = "mosquitto" } } });
        var setup = graph.GetEnvironment();
        var configuration = graph.ExportConfiguration();
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var topology = new EnvironmentTopology { Nodes =
        [
            new() { Id = "host", Kind = EnvironmentNodeKind.Host, Name = "Local", LocalHost = true },
            new() { Id = "network", Kind = EnvironmentNodeKind.Network, Name = "Office LAN" },
            new() { Id = "app", Kind = EnvironmentNodeKind.Application, Name = "Application", Host = "host", Networks = ["network"] },
            new() { Id = "broker", Kind = EnvironmentNodeKind.Service, Name = "Broker", Resource = "Resources:broker", Host = "host", Networks = ["network"], X = 240 }
        ] };
        graph.SetTopology(topology);
        var placed = graph.ToProjectJson();
        topology.Nodes.Clear();
        graph.GetEnvironmentDefinition().Topology.Nodes.Count.ShouldBe(4);
        var observedNodeCounts = new List<int>();
        graph.Changed += () => observedNodeCounts.Add(graph.GetEnvironmentDefinition().Topology.Nodes.Count);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(placed);
        observedNodeCounts.ShouldBe(new[] { 0, 4 });
        graph.SetTopology(new());
        graph.GetEnvironment().Resources["Resources:broker"].Provider.ShouldBe(setup.Resources["Resources:broker"].Provider);
        graph.ExportConfiguration().ShouldBe(configuration);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(placed);
        var revision = graph.Revision;
        graph.SetTopology(graph.GetEnvironmentDefinition().Topology);
        graph.Revision.ShouldBe(revision);
    }

    [Fact]
    public void Switching_environments_is_not_an_edit_and_undo_keeps_the_selected_environment()
    {
        var graph = StudioTestServices.CreateGraph();
        var local = graph.ActiveEnvironmentId;
        graph.AddEnvironment("Test");
        var test = graph.ActiveEnvironmentId;
        graph.ResetHistory();
        graph.SetEnvironment(new() { StartupTimeoutSeconds = 120 });
        graph.SelectEnvironment(local);
        graph.Undo();
        graph.ActiveEnvironmentId.ShouldBe(local);
        graph.CanUndo.ShouldBeFalse();
        graph.SelectEnvironment(test);
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(60);
        graph.Redo();
        graph.ActiveEnvironmentId.ShouldBe(test);
        graph.GetEnvironment().StartupTimeoutSeconds.ShouldBe(120);
    }

    [Fact]
    public async Task Unsupported_placement_is_rejected_without_calling_runner_or_changing_history()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.SetTopology(new() { Nodes =
        [
            new() { Id = "remote", Kind = EnvironmentNodeKind.Host, Name = "Other computer" },
            new() { Kind = EnvironmentNodeKind.Application, Name = "Application", Host = "remote" }
        ] });
        var original = graph.ToProjectJson();
        var revision = graph.Revision;
        var runner = new EnvironmentProjectTests.DelayedRunner();
        await using var session = new StudioRunSession(runner);
        await session.CheckAsync(graph);
        session.CheckResult!.Success.ShouldBeFalse();
        session.CheckResult.Message.ShouldContain("another host");
        await Should.ThrowAsync<InvalidOperationException>(() => session.StartAsync(graph));
        runner.Request.ShouldBeNull();
        graph.Revision.ShouldBe(revision);
        graph.ToProjectJson().ShouldBe(original);
    }

    [Fact]
    public async Task Switching_environment_rejects_a_late_check_result()
    {
        var graph = StudioTestServices.CreateGraph();
        var runner = new EnvironmentProjectTests.DelayedRunner();
        await using var session = new StudioRunSession(runner);
        var check = session.CheckAsync(graph);
        graph.AddEnvironment("Other");
        runner.CheckRelease.SetResult(new(true, "Old environment"));
        await check;
        session.CheckResult.ShouldBeNull();
    }

    [Fact]
    public void Invalid_topology_is_rejected_before_replacing_the_project()
    {
        var graph = StudioTestServices.CreateGraph();
        var original = graph.ToProjectJson();
        var topology = new EnvironmentTopology { Nodes =
        [
            new() { Id = "network", Kind = EnvironmentNodeKind.Network, Name = "LAN" },
            new() { Kind = EnvironmentNodeKind.Application, Name = "Application", Host = "network" }
        ] };
        Should.Throw<ArgumentException>(() => graph.SetTopology(topology));
        graph.ToProjectJson().ShouldBe(original);
        topology.Nodes[1].Host = null;
        graph.SetTopology(topology);
        var saved = graph.ToProjectJson();
        var invalid = JsonNode.Parse(saved)!;
        invalid["Environments"]![0]!["Topology"]!["Nodes"]![1]!["Networks"] = new JsonArray("missing");
        Should.Throw<ArgumentException>(() => graph.LoadProject(invalid.ToJsonString()));
        graph.ToProjectJson().ShouldBe(saved);
    }
}
