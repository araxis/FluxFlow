using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions;
using FluxFlow.Environments;
using FluxFlow.Plugins;
using FluxFlow.Plugins.Nats;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class NatsStudioProjectTests
{
    [Theory]
    [InlineData("jetstream.fluxflow", "main.publication.Output", "main.publisher.Input")]
    [InlineData("jetstream.fluxflow", "main.consumer.Output", "main.format.Input")]
    [InlineData("jetstream.fluxflow", "main.settlement.Output", "main.acknowledge.Input")]
    [InlineData("nats.fluxflow", "main.publication.Output", "main.request.Input")]
    [InlineData("nats.fluxflow", "main.request.Output", "main.format.Input")]
    [InlineData("nats.fluxflow", "main.receive.Output", "main.reply.Input")]
    public void Nats_wire_survives_reopen_and_undo_redo(string sample, string source, string target)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadProject(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", sample)));
        var saved = graph.ToProjectJson();
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(saved);
        reopened.ResetHistory();
        var wire = reopened.Diagram.Links.OfType<FlowLinkModel>()
            .Single(link => link.Original.Target == PortAddress.Parse(target));
        wire.Original.Source.ShouldBe(PortAddress.Parse(source));
        reopened.Diagram.SelectModel(wire, true);
        reopened.DeleteSelected();
        reopened.RecordEdit();
        var removed = reopened.ToProjectJson();
        removed.ShouldNotBe(saved);
        reopened.Undo();
        reopened.ToProjectJson().ShouldBe(saved);
        reopened.Redo();
        reopened.ToProjectJson().ShouldBe(removed);
    }

    [Theory]
    [InlineData("nats.publisher")]
    [InlineData("nats.jetstream.consumer")]
    [InlineData("nats.jetstream.settlement")]
    public void Portable_validation_reads_saved_fan_out_and_rejects_a_malformed_link(string type)
    {
        var component = new NatsPlugin().Description.Components.Single(c => c.Palette.ComponentType == type);
        var validate = new NatsDesign().Component(component).Validate!;
        using var settings = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:main:node:Type"] = type,
            ["Workflows:main:node:Connection"] = "Resources.broker",
            ["Workflows:main:node:Stream"] = "events",
            ["Workflows:main:node:Consumer"] = "worker",
            ["Workflows:main:node:Output:0"] = "first.Input",
            ["Workflows:main:node:Output:1:Address"] = "second.Input",
            ["Workflows:main:node:Output:1:Condition"] = "true"
        }).Build();
        var section = settings.GetSection("Workflows:main:node");
        validate(section);
        using var draft = (ConfigurationRoot)new ConfigurationBuilder()
            .AddInMemoryCollection(section.AsEnumerable(makePathsRelative: true)).Build();
        validate(draft);
        section["Output:1:Address"] = " ";
        Should.Throw<JsonException>(() => validate(section));
    }

    [Theory]
    [InlineData("nats.fluxflow")]
    [InlineData("jetstream.fluxflow")]
    public void Installed_catalog_round_trips_and_missing_plugin_preserves_all_saved_links(string sample)
    {
        var plugin = new NatsPlugin().Description;
        var wire = JsonSerializer.Serialize(new[] { plugin }, PluginJsonContext.Default.PluginDescriptionArray);
        var installed = JsonSerializer.Deserialize(wire, PluginJsonContext.Default.PluginDescriptionArray)!;
        installed.Single().Components.Count.ShouldBe(7);
        installed.Single().Connections.Single().Type.ShouldBe("nats.connection");
        var design = plugin.Design.ShouldNotBeNull();
        plugin.StudioEntry.ShouldBeNull();
        foreach (var name in design.Dependencies.Append(design.Assembly))
            System.Reflection.Assembly.Load(name).GetReferencedAssemblies().ShouldNotContain(a =>
                a.Name!.StartsWith("NATS.") || a.Name == "FluxFlow.Components.Nats" ||
                a.Name.Contains("Studio") || a.Name.Contains("AspNetCore.Components") || a.Name.Contains("MudBlazor"));

        var saved = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", sample));
        JsonNode? baseline = null;
        foreach (var catalog in new[] { installed, Array.Empty<PluginDescription>(), installed })
        {
            using var provider = new ServiceCollection().AddCatalogComponents(catalog).BuildServiceProvider();
            var graph = new DesignerGraphState(provider.GetServices<IComponentDescription>());
            graph.LoadProject(saved);
            graph.ResetHistory();
            (graph.FindDescription("nats.jetstream.settlement") is not null).ShouldBe(catalog.Length != 0);
            saved = graph.ToProjectJson();
            var reopened = JsonNode.Parse(saved)!;
            baseline ??= reopened;
            JsonNode.DeepEquals(reopened, baseline).ShouldBeTrue();
            graph.SetPortColorOverrides(new(Output: "#418dcb"));
            var edited = graph.ToProjectJson();
            graph.Undo(); JsonNode.DeepEquals(JsonNode.Parse(graph.ToProjectJson()), baseline).ShouldBeTrue();
            graph.Redo(); JsonNode.DeepEquals(JsonNode.Parse(graph.ToProjectJson()), JsonNode.Parse(edited)).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData("nats.fluxflow")]
    [InlineData("jetstream.fluxflow")]
    public void Sample_and_locked_profile_preserve_settings_through_undo_redo_and_reopen(string sample)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadProject(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", sample)));
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        var registry = new ImageRegistry { Address = "docker.io", Name = "Docker Hub" };
        var profile = new ContainerProfile { Name = "NATS", Provider = "nats", RegistryId = registry.Id, SetupLocked = true,
            Settings = new() { Image = "library/nats:2.11.8-alpine", Variables = new() { ["NATS_TOKEN_ENVIRONMENT_VARIABLE"] = "BROKER_TOKEN" },
                Files = new() { ["/etc/nats/nats.conf"] = "port: 4222\nauthorization { token: $NATS_TOKEN }\n" } } };
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = profile.CreateBinding([registry]) } });
        var saved = graph.ToProjectJson();
        profile.Settings.Variables.Clear(); profile.Settings.Files.Clear();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(saved);
        reopened.ToProjectJson().ShouldBe(saved);
        reopened.ExportConfiguration().ShouldBe(configuration);
        var binding = reopened.GetEnvironment().Resources["Resources:broker"];
        binding.ValidateSnapshot(); binding.Profile!.SetupLocked.ShouldBeTrue();
        binding.Variables["NATS_TOKEN_ENVIRONMENT_VARIABLE"].ShouldBe("BROKER_TOKEN");
        binding.Files["/etc/nats/nats.conf"].ShouldContain("$NATS_TOKEN");
        Should.Throw<ArgumentException>(() => (binding with { Image = "changed:latest" }).ValidateSnapshot());
    }

    [Fact]
    public void Existing_catalog_binding_is_protocol_checked_and_keeps_its_snapshot()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"nats.connection","Endpoint":"nats://original"}},"Workflows":{"main":{}}}""");
        graph.ResetHistory(); var before = graph.ToProjectJson();
        var host = new InfrastructureHost { Name = "Messaging", Services = [new() { Name = "NATS", Type = "nats.connection", Endpoint = "tls://broker.example:4222" }] };
        host.Validate();
        var topology = graph.GetEnvironmentDefinition().Topology;
        topology.PlaceHost(host, 100, 100);
        var service = topology.Nodes.Single(node => node.ExistingService != null);
        topology.Nodes.Add(new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Bindings = new() { ["Resources:broker"] = service.Id } });
        graph.SetTopology(topology); graph.EnvironmentLimitation().ShouldBeNull();
        var saved = graph.ToProjectJson(); host.Services.Clear();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(saved);
        reopened.CreateEnvironmentRunRequest().Instances.Single().ExistingServices["Resources:broker"].Endpoint.ShouldBe("tls://broker.example:4222");
        var changed = reopened.GetEnvironmentDefinition().Topology;
        var copied = changed.Nodes.Single(node => node.ExistingService != null);
        changed.Nodes[changed.Nodes.IndexOf(copied)] = copied with { ExistingService = copied.ExistingService! with { Endpoint = "nats://changed" } };
        Should.Throw<ArgumentException>(() => reopened.SetTopology(changed));
        reopened.ToProjectJson().ShouldBe(saved);
    }
}
