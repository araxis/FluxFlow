using System.Text.Json;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Environments.Topology;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class InfrastructureCatalogTests
{
    [Fact]
    public async Task Compiled_export_uses_the_run_environment_without_switching_the_editor_or_changing_history()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"https://manual/"}},"Workflows":{"main":{}}}""");
        var manualId = graph.ActiveEnvironmentId;
        graph.AddEnvironment("Bound endpoint");
        var boundId = graph.ActiveEnvironmentId;
        var topology = graph.GetEnvironmentDefinition().Topology;
        topology.Nodes.AddRange([
            new() { Kind = EnvironmentNodeKind.Host, Id = "host", Name = "Host", LocalHost = true },
            new() { Kind = EnvironmentNodeKind.Service, Id = "api", Name = "Service", Host = "host",
                ExistingService = new() { Type = "http.client", Endpoint = "https://bound/orders/" } },
            new() { Kind = EnvironmentNodeKind.Application, Id = "app", Name = "Application", Host = "host",
                Bindings = new() { ["Resources:api"] = "api" } }
        ]);
        graph.SetTopology(topology);
        graph.SelectEnvironment(manualId);
        var project = graph.ToProjectJson();
        var manual = graph.ExportConfiguration();
        (await graph.ExportCompiledConfigurationAsync(manualId)).ShouldBe(manual);
        using var bound = JsonDocument.Parse(await graph.ExportCompiledConfigurationAsync(boundId));
        bound.RootElement.GetProperty("Resources").GetProperty("api").GetProperty("BaseAddress").GetString().ShouldBe("https://bound/orders/");
        graph.ActiveEnvironmentId.ShouldBe(manualId);
        graph.ToProjectJson().ShouldBe(project);
        graph.Undo();
        graph.Redo();
        graph.ExportConfiguration().ShouldBe(manual);
        (await graph.ExportCompiledConfigurationAsync(boundId)).ShouldContain("https://bound/orders/");
    }

    [Theory]
    [InlineData("mqtt.client")]
    [InlineData("amqp.connection")]
    [InlineData("nats.connection")]
    public void Installed_provider_validates_endpoints_in_both_host_formats(string type)
    {
        var adapters = StudioTestServices.CreateGraph().ResourceAdapters;
        var host = new InfrastructureHost { Services = [new() { Type = type, Endpoint = "ftp://server:21" }] };
        var visualHost = host with { Topology = host.CreateTopology(), Services = [] };
        foreach (var saved in new[] { host, visualHost })
        {
            saved.Validate();
            Should.Throw<ArgumentException>(() => saved.Validate(adapters));
        }
    }

    [Fact]
    public void Applications_can_join_scaled_saved_hosts_and_contents_can_move_without_changing_definitions()
    {
        var graph = StudioTestServices.CreateGraph();
        var source = new InfrastructureHost { Name = "Server", Services = [new() { Name = "Broker", Endpoint = "mqtt://server:1883" }] };
        var sourceBefore = JsonSerializer.Serialize(source, EnvironmentJsonContext.Default.InfrastructureHost);
        var topology = graph.GetEnvironmentDefinition().Topology;
        var hostId = topology.PlaceHost(source, 100, 100);
        topology.Nodes.Single(node => node.Id == hostId).PlacementScale = .5;
        topology.Nodes.Add(new() { Id = "app", Kind = EnvironmentNodeKind.Application, Name = "Application", X = 1000, Y = 500 });
        graph.SetTopology(topology);
        graph.ResetHistory();
        var original = graph.ToProjectJson();
        var models = topology.Nodes.Select(node => new TopologyNodeModel(node, "", "")).ToArray();
        var service = models.Single(node => node.Definition.ExistingService is not null);
        service.AddConnectionPort(service.Definition.Id, "mqtt.client", "Broker");
        TopologyContainers.Arrange(models);
        var host = models.Single(node => node.Definition.Id == hostId);
        var app = models.Single(node => node.Definition.Id == "app");
        app.SetPosition(170, 150);
        TopologyContainers.Drop(app, models);
        app.Definition.Host.ShouldBe(hostId);
        app.Container.ShouldBe(host);
        app.DisplayScale.ShouldBe(.5);
        app.Size!.Width.ShouldBe(90);
        var serviceX = service.Capture().X;
        service.SetPosition(service.Position.X + 20, service.Position.Y);
        service.Capture().X.ShouldBe(serviceX + 40);
        service.SetPosition(9000, 9000);
        service.Position.X.ShouldBeLessThanOrEqualTo(host.Position.X + host.Size!.Width - service.Size!.Width - 12);
        service.Position.Y.ShouldBeLessThanOrEqualTo(host.Position.Y + host.Size!.Height - service.Size!.Height - 12);
        var arrangedService = service.Capture();
        host.ResizeBounds(host.Position.X, host.Position.Y, host.Position.X + 390, host.Position.Y + 180, "se");
        service.Capture().X.ShouldBe(arrangedService.X, .000001);
        service.Capture().Y.ShouldBe(arrangedService.Y, .000001);
        graph.SetTopology(new() { Nodes = models.Select(node => node.Capture()).ToList() });
        var saved = graph.ToProjectJson();
        graph.GetEnvironmentDefinition().Topology.Nodes.Single(node => node.Id == "app").Host.ShouldBe(hostId);
        graph.EnvironmentLimitation()!.ShouldContain("another host");
        graph.SetLocalExecutionHost(hostId, true);
        graph.EnvironmentLimitation().ShouldBeNull();
        graph.Undo(); graph.ToProjectJson().ShouldBe(saved);
        graph.Undo(); graph.ToProjectJson().ShouldBe(original);
        graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(saved);
        var restored = reopened.GetEnvironmentDefinition().Topology;
        restored.Nodes.Single(node => node.Id == service.Definition.Id).ExistingService.ShouldBe(source.Services[0]);
        restored.Nodes.RemoveAll(node => node.Id == service.Definition.Id);
        Should.Throw<ArgumentException>(() => reopened.SetTopology(restored));
        JsonSerializer.Serialize(source, EnvironmentJsonContext.Default.InfrastructureHost).ShouldBe(sourceBefore);
    }

    public static IEnumerable<object?[]> HostResizeCases =>
        from edge in new[] { "e", "s", "w", "n", "ne", "nw", "se", "sw" }
        from scale in new double?[] { null, .2, .5, 1.5, 4 }
        select new object?[] { edge, scale };

    [Theory]
    [MemberData(nameof(HostResizeCases))]
    public void Placement_resize_keeps_contents_fixed_preserves_scaling_and_roundtrips_with_history(string edge, double? savedScale)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"http://original/"}},"Workflows":{"main":{}}}""");
        var config = graph.ExportConfiguration();
        var source = new InfrastructureHost { Name = "Server", Topology = new() { Nodes =
        [
            new() { Id = "host", Name = "Server", Kind = EnvironmentNodeKind.Host, Width = 800, Height = 600 },
            new() { Id = "net", Name = "LAN", Kind = EnvironmentNodeKind.Network, Host = "host", X = 40, Y = 100, Width = 700, Height = 440 },
            new() { Id = "service", Name = "API", Kind = EnvironmentNodeKind.Service, Host = "host", Networks = ["net"], X = 80, Y = 200,
                ExistingService = new() { Name = "API", Type = "http.client", Endpoint = "https://server/api/" } }
        ] } };
        var sourceBefore = JsonSerializer.Serialize(source, EnvironmentJsonContext.Default.InfrastructureHost);
        var topology = graph.GetEnvironmentDefinition().Topology;
        var id = topology.PlaceHost(source, 100, 100);
        topology.Nodes.Single(node => node.Id == id).PlacementScale = savedScale;
        var serviceId = topology.Nodes.Single(node => node.ExistingService is not null).Id;
        topology.Nodes.Add(new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application,
            Host = id, X = 400, Y = 220,
            Bindings = new() { ["Resources:api"] = serviceId } });
        graph.SetTopology(topology);
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var models = topology.Nodes.Select(node => new TopologyNodeModel(node, "", "")).ToArray();
        var service = models.Single(node => node.Definition.Id == serviceId);
        service.AddConnectionPort(serviceId, "http.client", "API");
        TopologyContainers.Arrange(models);
        var root = models.Single(node => node.Definition.Id == id);
        root.CanResize.ShouldBeTrue();
        service.CanResize.ShouldBeFalse();
        var scale = savedScale ?? 1;
        var contents = models.Where(node => node != root).ToDictionary(node => node.Definition.Id,
            node => (node.Position, Size: node.Size!));
        var left = edge.Contains('w') ? 0 : 100;
        var top = edge.Contains('n') ? 20 : 100;
        var right = 100 + 800 * scale + (edge.Contains('e') ? 150 : 0);
        var bottom = 100 + 600 * scale + (edge.Contains('s') ? 120 : 0);
        root.ResizeBounds(left, top, right, bottom, edge);
        root.Position.X.ShouldBe(left);
        root.Position.Y.ShouldBe(top);
        root.Size!.Width.ShouldBe(right - left);
        root.Size.Height.ShouldBe(bottom - top);
        void CheckContents(IEnumerable<TopologyNodeModel> nodes)
        {
            foreach (var node in nodes.Where(node => contents.ContainsKey(node.Definition.Id)))
            {
                var original = contents[node.Definition.Id];
                node.Position.X.ShouldBe(original.Position.X, .000001);
                node.Position.Y.ShouldBe(original.Position.Y, .000001);
                node.Size!.Width.ShouldBe(original.Size.Width, .000001);
                node.Size.Height.ShouldBe(original.Size.Height, .000001);
                node.DisplayScale.ShouldBe(scale);
            }
        }
        CheckContents(models);
        root.ResizeBounds(edge.Contains('w') ? right - 1 : left, edge.Contains('n') ? bottom - 1 : top,
            edge.Contains('e') ? left + 1 : right, edge.Contains('s') ? top + 1 : bottom, edge);
        foreach (var child in root.Children)
        {
            (child.Position.X - root.Position.X).ShouldBeGreaterThanOrEqualTo(24 * scale - .000001);
            (child.Position.Y - root.Position.Y).ShouldBeGreaterThanOrEqualTo(76 * scale - .000001);
            (root.Position.X + root.Size.Width - child.Position.X - child.Size!.Width).ShouldBeGreaterThanOrEqualTo(24 * scale - .000001);
            (root.Position.Y + root.Size.Height - child.Position.Y - child.Size.Height).ShouldBeGreaterThanOrEqualTo(24 * scale - .000001);
        }
        CheckContents(models);
        root.ResizeBounds(left, top, right, bottom, edge);
        TopologyContainers.Arrange(models);
        root.Size.Width.ShouldBe(right - left, .000001);
        root.Size.Height.ShouldBe(bottom - top, .000001);
        CheckContents(models);
        graph.SetTopology(new() { Nodes = models.Select(node => node.Capture()).ToList() });
        var resized = graph.ToProjectJson();
        var saved = graph.GetEnvironmentDefinition().Topology;
        saved.Nodes.Single(node => node.Id == id).Width.ShouldBe((right - left) / scale);
        saved.Nodes.Single(node => node.Id == id).PlacementScale.ShouldBe(savedScale);
        saved.Nodes.Single(node => node.Id == serviceId).ExistingService.ShouldBe(source.Topology.Nodes[2].ExistingService);
        graph.ExportConfiguration().ShouldBe(config);
        graph.CreateEnvironmentRunRequest().Instances.Single().ExistingServices["Resources:api"].Endpoint.ShouldBe("https://server/api/");
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(resized);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(resized);
        var restored = reopened.GetEnvironmentDefinition().Topology.Nodes.Select(node => new TopologyNodeModel(node, "", "")).ToArray();
        restored.Single(node => node.Definition.Id == serviceId).AddConnectionPort(serviceId, "http.client", "API");
        TopologyContainers.Arrange(restored);
        var restoredRoot = restored.Single(node => node.Definition.Id == id);
        restoredRoot.Size!.Width.ShouldBe(right - left, .000001);
        restoredRoot.Size.Height.ShouldBe(bottom - top, .000001);
        CheckContents(restored);
        restoredRoot.Capture().PlacementScale.ShouldBe(savedScale);
        JsonSerializer.Serialize(source, EnvironmentJsonContext.Default.InfrastructureHost).ShouldBe(sourceBefore);
    }

    [Theory]
    [MemberData(nameof(HostResizeCases))]
    public void Resizing_an_automatically_framed_host_preserves_its_displayed_bounds_on_reopen(string edge, double? scale)
    {
        var models = new EnvironmentNode[]
        {
            new() { Id = "host", Kind = EnvironmentNodeKind.Host, Name = "Host", X = 20, Y = 30,
                InfrastructureGroup = scale is null ? null : "host", PlacementScale = scale },
            new() { Id = "service", Kind = EnvironmentNodeKind.Service, Name = "Service", X = 180, Y = 200,
                Host = "host", InfrastructureGroup = scale is null ? null : "host" }
        }.Select(node => new TopologyNodeModel(node, "", "")).ToArray();
        TopologyContainers.Arrange(models);
        var host = models[0];
        host.ResizeBounds(host.Position.X - (edge.Contains('w') ? 100 : 0), host.Position.Y - (edge.Contains('n') ? 100 : 0),
            host.Position.X + host.Size!.Width + (edge.Contains('e') ? 100 : 0),
            host.Position.Y + host.Size.Height + (edge.Contains('s') ? 100 : 0), edge);
        var restored = models.Select(node => new TopologyNodeModel(node.Capture(), "", "")).ToArray();
        TopologyContainers.Arrange(restored);
        for (var index = 0; index < models.Length; index++)
        {
            restored[index].Position.X.ShouldBe(models[index].Position.X, .000001);
            restored[index].Position.Y.ShouldBe(models[index].Position.Y, .000001);
            restored[index].Size!.Width.ShouldBe(models[index].Size!.Width, .000001);
            restored[index].Size!.Height.ShouldBe(models[index].Size!.Height, .000001);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(double.NaN)]
    public void Invalid_host_placement_scales_are_rejected(double scale)
    {
        var topology = new EnvironmentTopology();
        var id = topology.PlaceHost(new(), 0, 0);
        topology.Nodes.Single(node => node.Id == id).PlacementScale = scale;
        Should.Throw<ArgumentException>(topology.Validate);
    }

    [Fact]
    public void Ble_peripheral_snapshots_bind_without_changing_connections_and_reject_adapter_host_mismatch()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"sensor":{"Type":"ble.peripheral","Endpoint":"ble://010203040506/public"},"radio":{"Type":"ble.adapter","Endpoint":"ble-adapter://default"}},"Workflows":{"main":{}}}""");
        var config = graph.ExportConfiguration();
        graph.ResetHistory(); var before = graph.ToProjectJson();
        var host = new InfrastructureHost { Name = "Lab", Services = [
            new() { Name = "Sensor", Type = "ble.peripheral", Endpoint = "ble://112233445566/random" },
            new() { Name = "Radio", Type = "ble.adapter", Endpoint = "ble-adapter://default" }] };
        host.Validate();
        var topology = graph.GetEnvironmentDefinition().Topology;
        topology.PlaceHost(host, 100, 100);
        topology.Nodes.Add(new() { Id = "local", Name = "This computer", Kind = EnvironmentNodeKind.Host, LocalHost = true });
        var app = new EnvironmentNode { Id = "app", Name = "App", Kind = EnvironmentNodeKind.Application, Host = "local" };
        topology.Nodes.Add(app);
        app.Bindings["Resources:sensor"] = topology.Nodes.Single(node => node.ExistingService?.Type == "ble.peripheral").Id;
        graph.SetTopology(topology);
        graph.EnvironmentLimitation().ShouldBeNull();
        var saved = graph.ToProjectJson();
        graph.ExportConfiguration().ShouldBe(config);
        graph.CreateEnvironmentRunRequest().Instances.Single().ExistingServices["Resources:sensor"].Endpoint.ShouldBe("ble://112233445566/random");
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(saved);
        reopened.ExportConfiguration().ShouldBe(config);
        app.Bindings["Resources:radio"] = topology.Nodes.Single(node => node.ExistingService?.Type == "ble.adapter").Id;
        TopologyConnections.Check(topology, graph.Connections, graph.ResourceAdapters)!.Message.ShouldContain("execution host");
        var radio = topology.Nodes.FindIndex(node => node.ExistingService?.Type == "ble.adapter");
        topology.Nodes[radio] = topology.Nodes[radio] with { Resource = "Resources:radio" };
        app.Bindings.Remove("Resources:radio");
        TopologyConnections.Check(topology, graph.Connections, graph.ResourceAdapters)!.Message.ShouldContain("execution host");
        app.Bindings["Resources:radio"] = topology.Nodes[radio].Id;
        app.Bindings["Resources:sensor"] = app.Bindings["Resources:radio"];
        TopologyConnections.Check(topology, graph.Connections, graph.ResourceAdapters)!.Message.ShouldContain("incompatible");
        var changed = reopened.GetEnvironmentDefinition().Topology;
        var sensor = changed.Nodes.FindIndex(node => node.ExistingService?.Type == "ble.peripheral");
        changed.Nodes[sensor] = changed.Nodes[sensor] with { ExistingService = changed.Nodes[sensor].ExistingService! with { Endpoint = "ble://112233445577/public" } };
        Should.Throw<ArgumentException>(() => reopened.SetTopology(changed));
        reopened.ToProjectJson().ShouldBe(saved);
    }

    [Fact]
    public void Visual_snapshots_remap_networks_and_move_as_one_unit_with_undo()
    {
        var source = new InfrastructureHost { Name = "P1", Topology = new() { Nodes =
        [
            new() { Id = "host", Name = "P1", Kind = EnvironmentNodeKind.Host, Width = 700, Height = 500 },
            new() { Id = "net", Name = "LAN", Kind = EnvironmentNodeKind.Network, Host = "host", X = 30, Y = 80, Width = 600, Height = 350 },
            new() { Id = "service", Name = "API", Kind = EnvironmentNodeKind.Service, Host = "host", Networks = ["net"], X = 60, Y = 170,
                ExistingService = new() { Name = "API", Type = "http.client", Endpoint = "https://p1/api/" }, Color = "#123456" }
        ] } };
        var graph = StudioTestServices.CreateGraph();
        var topology = graph.GetEnvironmentDefinition().Topology;
        var root = topology.PlaceHost(source, 100, 100);
        graph.SetTopology(topology);
        graph.ResetHistory();
        var saved = graph.ToProjectJson();
        var models = topology.Nodes.Select(node => new TopologyNodeModel(node, "", "")).ToArray();
        TopologyContainers.Arrange(models);
        models.Single(node => node.Definition.Id == root).SetPosition(140, 160);
        graph.SetTopology(new() { Nodes = models.Select(node => node.Capture()).ToList() });
        var moved = graph.ToProjectJson();
        graph.GetEnvironmentDefinition().Topology.Nodes.Single(node => node.ExistingService is not null).X.ShouldBe(200);
        graph.Undo(); graph.ToProjectJson().ShouldBe(saved);
        graph.Redo(); graph.ToProjectJson().ShouldBe(moved);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(moved);
        var snapshot = reopened.GetEnvironmentDefinition().Topology;
        snapshot.Nodes.ShouldAllBe(node => node.InfrastructureGroup == root);
        snapshot.Nodes.Single(node => node.ExistingService is not null).Networks.Single().ShouldNotBe("net");
        source.Topology.Nodes.Clear();
        snapshot.Nodes.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("endpoint")]
    [InlineData("resize")]
    [InlineData("unlock")]
    [InlineData("add-inside")]
    public void Project_edits_cannot_change_infrastructure_internals(string edit)
    {
        var graph = StudioTestServices.CreateGraph();
        var topology = graph.GetEnvironmentDefinition().Topology;
        var root = topology.PlaceHost(new() { Services = [new()] }, 0, 0);
        graph.SetTopology(topology);
        graph.ResetHistory();
        var saved = graph.ToProjectJson();
        var service = topology.Nodes.Single(node => node.ExistingService is not null);
        if (edit == "remove") topology.Nodes.Remove(service);
        if (edit == "endpoint") topology.Nodes[1] = service with { ExistingService = service.ExistingService! with { Endpoint = "mqtt://changed:1883" } };
        if (edit == "resize") service.Width = 260;
        if (edit == "unlock") topology = new() { Nodes = topology.Nodes.Select(node => node with { InfrastructureGroup = null }).ToList() };
        if (edit == "add-inside") topology.Nodes.Add(new() { Name = "Service", Kind = EnvironmentNodeKind.Service, Host = root, Resource = "Resources:broker" });
        Should.Throw<ArgumentException>(() => graph.SetTopology(topology));
        graph.ToProjectJson().ShouldBe(saved);
        graph.CanUndo.ShouldBeFalse();
    }
    [Fact]
    public void PlaceHost_preserves_the_drop_point_for_hosts_defined_far_from_the_origin()
    {
        var source = new InfrastructureHost().CreateTopology();
        source.Nodes[0].X = 1e16;
        var topology = new EnvironmentTopology();
        topology.PlaceHost(new() { Topology = source }, 1, 2);
        topology.Nodes.Single().X.ShouldBe(1);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.MaxValue)]
    public void PlaceHost_rejects_invalid_positions_without_leaving_a_partial_copy(double x)
    {
        var topology = new EnvironmentTopology();
        topology.PlaceHost(new(), 1, 2);
        var original = topology.Nodes.Single();
        var source = new InfrastructureHost { Services = [new()] }.CreateTopology();
        source.Nodes[1].X = double.MaxValue;
        Should.Throw<ArgumentException>(() => topology.PlaceHost(new() { Topology = source }, x, 0));
        topology.Nodes.Single().ShouldBeSameAs(original);
    }

    [Fact]
    public void Placement_and_bindings_are_independent_snapshots_with_undo_and_save_reopen()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://original:1883"},"api":{"Type":"http.client","BaseAddress":"http://original/"}},"Workflows":{"main":{}}}""");
        var configuration = graph.ExportConfiguration();
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var host = new InfrastructureHost { Name = "P1", Services =
        [
            new() { Name = "RabbitMQ", Endpoint = "mqtt://p1:1883" },
            new() { Name = "API", Type = "http.client", Endpoint = "https://p1/api/" }
        ] };
        var defaults = new EnvironmentDefaults { Infrastructure = [host] };
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(defaults, EnvironmentJsonContext.Default.EnvironmentDefaults), EnvironmentJsonContext.Default.EnvironmentDefaults)!;
        restored.Infrastructure.Single().Services.Count.ShouldBe(2);
        var definition = host.CreateTopology();
        definition.Nodes.Add(new() { Id = "lan", Kind = EnvironmentNodeKind.Network, Name = "LAN", Host = definition.Nodes[0].Id });
        definition.Nodes[1].Networks.Add("lan");
        defaults = defaults.UpdateHost(host, definition);
        host = defaults.Infrastructure.Single();
        var topology = graph.GetEnvironmentDefinition().Topology;
        var first = topology.PlaceHost(host, 50, 70);
        graph.SetTopology(topology);
        var firstPlacement = graph.ToProjectJson();
        var hostId = topology.PlaceHost(host, 650, 70);
        graph.SetTopology(topology);
        var placed = graph.ToProjectJson();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(firstPlacement);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(firstPlacement);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(placed);
        topology = graph.GetEnvironmentDefinition().Topology;
        var services = topology.Nodes.Where(node => node.Host == hostId && node.ExistingService is not null).ToArray();
        services.ShouldAllBe(service => service.Host == hostId && service.Resource == null);
        topology.Nodes.Add(new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application,
            Bindings = new() { ["Resources:broker"] = services[0].Id, ["Resources:api"] = services[1].Id } });
        graph.SetTopology(topology);
        graph.EnvironmentLimitation().ShouldBeNull();
        var bound = graph.ToProjectJson();
        var edited = host.CreateTopology();
        edited.Nodes[1] = edited.Nodes[1] with { ExistingService = edited.Nodes[1].ExistingService! with { Endpoint = "mqtt://replacement:1883" } };
        defaults = defaults.UpdateHost(host, edited);
        defaults.Infrastructure.Single().Topology!.Nodes[1].ExistingService!.Endpoint.ShouldBe("mqtt://replacement:1883");
        defaults = defaults.RemoveHost(defaults.Infrastructure.Single());
        defaults.Infrastructure.ShouldBeEmpty();
        graph.ToProjectJson().ShouldBe(bound);
        graph.CreateEnvironmentRunRequest().Instances.Single().ExistingServices["Resources:api"].Endpoint.ShouldBe("https://p1/api/");
        graph.CreateEnvironmentRunRequest().Instances.Single().Bindings.ShouldBeEmpty();
        graph.ExportConfiguration().ShouldBe(configuration);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(bound);
        reopened.ResetHistory();
        reopened.ToProjectJson().ShouldBe(bound);
        var nodes = reopened.GetEnvironmentDefinition().Topology.Nodes;
        var app = nodes.Single(node => node.Id == "app");
        app.Bindings["Resources:broker"].ShouldBe(services[0].Id);
        app.Bindings["Resources:api"].ShouldBe(services[1].Id);
        foreach (var root in new[] { first, hostId })
        {
            var broker = nodes.Single(node => node.Host == root && node.ExistingService?.Type == "mqtt.client");
            broker.Networks.Single().ShouldBe(nodes.Single(node => node.Host == root && node.Kind == EnvironmentNodeKind.Network).Id);
            broker.ExistingService!.Endpoint.ShouldBe("mqtt://p1:1883");
        }
        reopened.CreateEnvironmentRunRequest().Instances.Single().ExistingServices["Resources:broker"].Endpoint.ShouldBe("mqtt://p1:1883");
        var swapped = reopened.GetEnvironmentDefinition().Topology;
        var firstBroker = swapped.Nodes.Single(node => node.Host == first && node.ExistingService?.Type == "mqtt.client");
        var secondBroker = swapped.Nodes.Single(node => node.Host == hostId && node.ExistingService?.Type == "mqtt.client");
        var firstIndex = swapped.Nodes.IndexOf(firstBroker);
        var secondIndex = swapped.Nodes.IndexOf(secondBroker);
        swapped.Nodes[firstIndex] = firstBroker with { Id = secondBroker.Id };
        swapped.Nodes[secondIndex] = secondBroker with { Id = firstBroker.Id };
        swapped.Validate(); // Both groups still contain a broker, but the binding would now reach the first copy.
        Should.Throw<ArgumentException>(() => reopened.SetTopology(swapped));
        reopened.CanUndo.ShouldBeFalse();
        reopened.ToProjectJson().ShouldBe(bound);
        var withoutFirst = reopened.GetEnvironmentDefinition().Topology;
        withoutFirst.Nodes.RemoveAll(node => node.InfrastructureGroup == first);
        reopened.SetTopology(withoutFirst);
        reopened.EnvironmentLimitation().ShouldBeNull();
        reopened.GetEnvironmentDefinition().Topology.Nodes.Single(node => node.Id == "app")
            .Bindings["Resources:broker"].ShouldBe(services[0].Id);
        var remainingProject = reopened.ToProjectJson();
        reopened.Undo(); reopened.ToProjectJson().ShouldBe(bound);
        reopened.Redo(); reopened.ToProjectJson().ShouldBe(remainingProject);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(placed);
        graph.Redo();
        graph.ToProjectJson().ShouldBe(bound);
        var alternate = graph.ActiveEnvironmentId;
        graph.AddEnvironment("Other");
        graph.GetEnvironmentDefinition().Topology.Nodes.ShouldBeEmpty();
        graph.SelectEnvironment(alternate);
        graph.GetEnvironmentDefinition().Topology.Nodes.Count.ShouldBe(9);
        topology.Nodes.Single(node => node.Id == "app").Bindings["Resources:broker"] = services[1].Id;
        graph.SetTopology(topology);
        graph.EnvironmentLimitation()!.ShouldContain("incompatible");
        topology.Nodes.RemoveAll(node => node.InfrastructureGroup == hostId);
        graph.SetTopology(topology);
        graph.EnvironmentLimitation()!.ShouldContain("unbound");
    }

    [Fact]
    public void Repeated_placements_have_distinct_ids_and_existing_endpoints_ignore_run_addresses()
    {
        var source = new InfrastructureHost { Services = [new() { Endpoint = "mqtt://localhost:1883" }] };
        var definition = new EnvironmentDefinition();
        definition.Topology.PlaceHost(source, 0, 0);
        definition.Topology.PlaceHost(source, 600, 0);
        definition.Validate();
        definition.Topology.Nodes.Select(node => node.Id).Distinct().Count().ShouldBe(4);
        var service = definition.Topology.Nodes.First(node => node.ExistingService is not null);
        var endpoint = TopologyEndpoint.Resolve(definition, service, null, new("unused", "mosquitto", "mqtt://other:1883", "container"));
        endpoint.Value.ShouldBe(source.Services[0].Endpoint);
        endpoint.Warning.ShouldNotBeNull();
        TopologyRunState.Resolve(service, definition, new(Guid.NewGuid(), "Stopped", []))!.Label.ShouldBe("External");
    }

    [Fact]
    public void Catalog_retains_an_uninstalled_service_but_execution_rejects_its_binding()
    {
        var service = new InfrastructureService { Type = "external.device", Endpoint = "https://host/" };
        new EnvironmentDefaults { Infrastructure = [new() { Services = [service] }] }.Validate();
        var request = new EnvironmentRunRequest("""{"Resources":{"device":{"Type":"external.device"}}}""", new())
        { Instances = [new("app", "App") { ExistingServices = new() { ["Resources:device"] = service } }] };
        Should.Throw<ArgumentException>(() => new EnvironmentPreparation([], []).ValidateBindings(request));
    }

    [Theory]
    [InlineData("http.client", "mqtt://host:1883")]
    [InlineData("mqtt.client", "mqtt://user:secret@host:1883")]
    [InlineData("mqtt.client", "mqtt://host/topic")]
    [InlineData("http.client", "relative")]
    public void Invalid_or_credential_bearing_catalog_endpoints_are_rejected(string type, string endpoint)
    {
        var catalog = new EnvironmentDefaults { Infrastructure = [new() { Services = [new() { Type = type, Endpoint = endpoint }] }] };
        Should.Throw<ArgumentException>(() => catalog.Validate([new HttpEnvironmentAdapter(), new MqttEnvironmentAdapter()]));
    }
}
