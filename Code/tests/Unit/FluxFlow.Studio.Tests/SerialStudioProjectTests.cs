using System.Text.Json.Nodes;
using FluxFlow.Application.Addressing;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class SerialStudioProjectTests
{
    [Fact]
    public void Serial_text_event_wiring_round_trips_independently_of_data_and_error_wiring()
    {
        const string configuration = """
            {"Workflows":{"device":{
              "Resources":{"port":{"Type":"serial.port","PortName":"loopback","BaudRate":9600}},
              "command":{"Type":"mapper","Engine":"jsonata","Expression":"$","Output":"text.Input"},
              "text":{"Type":"serial.text","Connection":"port","NewLine":"\r\n","MaxLineBytes":6,
                      "Output":"lines.Input","Events":"events.Input","Error":"errors.Input"},
              "lines":{"Type":"mapper","Engine":"jsonata","Expression":"$"},
              "events":{"Type":"mapper","Engine":"jsonata","Expression":"$"},
              "errors":{"Type":"mapper","Engine":"jsonata","Expression":"$","HandleErrors":true}
            }}}
            """;
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(configuration);
        var saved = graph.ToProjectJson();
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(saved);
        reopened.ResetHistory();
        var link = reopened.Diagram.Links.OfType<FlowLinkModel>()
            .Single(l => l.Original.Source == PortAddress.Parse("device.text.Events"));
        reopened.Diagram.SelectModel(link, true);
        reopened.DeleteSelected();
        reopened.RecordEdit();
        var edited = reopened.ToProjectJson();
        var expected = JsonNode.Parse(configuration)!;
        expected["Workflows"]!["device"]!["text"]!.AsObject().Remove("Events");
        JsonNode.DeepEquals(JsonNode.Parse(reopened.ToJson()), expected).ShouldBeTrue();
        reopened.Undo();
        reopened.ToProjectJson().ShouldBe(saved);
        JsonNode.DeepEquals(JsonNode.Parse(reopened.ToJson()), JsonNode.Parse(configuration)).ShouldBeTrue();
        reopened.Redo();
        reopened.ToProjectJson().ShouldBe(edited);
    }

    private const string Config = """{"Workflows":{"device":{"Resources":{"port":{"Type":"serial.port","PortName":"original","BaudRate":9600}},"read":{"Type":"serial.reader","Connection":"port"},"write":{"Type":"serial.writer","Connection":"port"}}}}""";
    [Fact]
    public void Physical_catalog_host_binding_is_project_owned_undoable_and_preserves_locked_device()
    {
        var graph = StudioTestServices.CreateGraph(); graph.LoadJson(Config); graph.ResetHistory();
        var host = new InfrastructureHost { Name = "Bench", Services = [new() { Name = "Loopback", Type = "serial.port", Endpoint = "serial-port://local/COM3" }] };
        var topology = graph.GetEnvironmentDefinition().Topology;
        var hostId = topology.PlaceHost(host, 100, 100);
        var service = topology.Nodes.Single(item => item.ExistingService != null);
        topology.Nodes.Add(new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application,
            Bindings = new() { ["Workflows:device:Resources:port"] = service.Id } });
        graph.SetTopology(topology); graph.EnvironmentLimitation().ShouldContain("execution host");
        var before = graph.ToProjectJson(); graph.SetLocalExecutionHost(hostId, true);
        graph.EnvironmentLimitation().ShouldBeNull(); var saved = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(saved);
        reopened.EnvironmentLimitation().ShouldBeNull();
        reopened.CreateEnvironmentRunRequest().Instances.Single().ExistingServices["Workflows:device:Resources:port"].Endpoint.ShouldBe("serial-port://local/COM3");
        var changed = reopened.GetEnvironmentDefinition().Topology;
        var index = changed.Nodes.FindIndex(node => node.Id == service.Id);
        changed.Nodes[index] = changed.Nodes[index] with { ExistingService = service.ExistingService! with { Endpoint = "serial-port://local/COM4" } };
        Should.Throw<ArgumentException>(() => reopened.SetTopology(changed));
        reopened.ToProjectJson().ShouldBe(saved);
        host.CreateTopology().Nodes.ShouldAllBe(node => !node.LocalHost);
    }

    [Fact]
    public void Serial_connection_editor_uses_runtime_validation_without_opening_devices()
    {
        var description = StudioTestServices.SerialConnection();
        description.RequiresWorkflowScope.ShouldBeTrue(); var settings = description.Create();
        settings["PortName"] = "UnavailableTestPort"; description.Validate!(settings);
        settings["BaudRate"] = 0; Should.Throw<ArgumentException>(() => description.Validate(settings));
    }

    [Fact]
    public void Sample_has_reader_writer_guides_and_roundtrips_unchanged()
    {
        var graph = StudioTestServices.CreateGraph(); graph.LoadProject(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "serial.fluxflow")));
        var configuration = graph.ExportConfiguration(); var saved = graph.ToProjectJson();
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(saved);
        reopened.ExportConfiguration().ShouldBe(configuration);
        foreach (var type in new[] { "serial.reader", "serial.writer" })
        {
            var description = graph.FindDescription(type)!;
            description.Documentation.ShouldContain("## Ports");
        }
    }
}
