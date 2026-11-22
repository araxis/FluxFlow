using System.Text.Json.Nodes;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Connections;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ConnectionLifecycleTests
{
    [Fact]
    public void Rename_into_an_existing_group_preserves_its_saved_casing_and_updates_references()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client","Password":"retained-test-value"},"Services":{"api":{"Type":"http.client"}}},"Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"broker"}}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        graph.RenameConnection("Resources:broker", "services.messaging").ShouldBe("Resources:Services:messaging");
        var after = graph.ToProjectJson();
        graph.Connections.Single(item => item.Name == "Services.messaging").Settings["Password"]!.GetValue<string>().ShouldBe("retained-test-value");
        JsonNode.Parse(graph.ToJson())!["Workflows"]!["main"]!["receive"]!["Connection"]!.GetValue<string>().ShouldBe("Resources.Services.messaging");
        graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(after); reopened.ToProjectJson().ShouldBe(after);
    }

    private const string Configuration = """
        {"Keep":42,"FluxFlow":{"resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://localhost","Credentials":{"Username":"trial","Password":"preserve-this-test-value"},"Custom":{"Keep":true}}},
        "Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"broker"},"custom":{"Type":"custom.unknown","Connection":"broker"}},
        "other":{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://other"}},"local":{"Type":"mqtt.subscriber","Connection":"broker"},"shared":{"Type":"mqtt.subscriber","Connection":"Resources.broker"}}}}}
        """;

    [Fact]
    public void Rename_updates_known_references_and_every_environment_atomically_and_round_trips()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Configuration, "FluxFlow");
        var originalSettings = graph.Connections.Single(item => item.Workflow is null).Settings.ToJsonString();
        void Setup()
        {
            graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = new() { Provider = "existing" } } });
            graph.SetTopology(new() { Nodes = [
                new() { Id = "service", Kind = EnvironmentNodeKind.Service, Resource = "Resources:broker", Name = "Broker" },
                new() { Id = "app", Kind = EnvironmentNodeKind.Application, Name = "Application", Bindings = new() { ["Resources:broker"] = "service" } }
            ] });
        }
        Setup(); graph.AddEnvironment("Second"); Setup(); graph.ResetHistory();
        var before = graph.ToProjectJson();
        graph.RenameConnection("Resources:broker", "messaging.broker").ShouldBe("Resources:messaging:broker");
        var after = graph.ToProjectJson();
        graph.Connections.Single(item => item.Workflow is null).Settings.ToJsonString().ShouldBe(originalSettings);
        var workflows = JsonNode.Parse(graph.ToJson())!["FluxFlow"]!["Workflows"]!;
        workflows["main"]!["receive"]!["Connection"]!.GetValue<string>().ShouldBe("Resources.messaging.broker");
        workflows["main"]!["custom"]!["Connection"]!.GetValue<string>().ShouldBe("broker");
        workflows["other"]!["local"]!["Connection"]!.GetValue<string>().ShouldBe("broker");
        workflows["other"]!["shared"]!["Connection"]!.GetValue<string>().ShouldBe("Resources.messaging.broker");
        foreach (var environment in graph.Environments)
        {
            environment.Setup.Resources.ShouldContainKey("Resources:messaging:broker");
            environment.Setup.Resources.ShouldNotContainKey("Resources:broker");
            environment.Topology.Nodes.Single(item => item.Id == "service").Resource.ShouldBe("Resources:messaging:broker");
            environment.Topology.Nodes.Single(item => item.Id == "app").Bindings["Resources:messaging:broker"].ShouldBe("service");
        }
        graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(after);
        reopened.ToProjectJson().ShouldBe(after);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rename_rejects_saved_environment_key_collisions_without_changing_configuration(bool binding)
    {
        var graph = StudioTestServices.CreateGraph(); graph.LoadJson(Configuration, "FluxFlow");
        if (binding)
            graph.SetTopology(new() { Nodes = [new()
            {
                Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application,
                Bindings = new() { ["Resources:broker"] = "original-service", ["Resources:renamed"] = "saved-service" }
            }] });
        else
            graph.SetEnvironment(new() { Resources = new()
            {
                ["Resources:broker"] = new() { Provider = "existing" },
                ["Resources:renamed"] = new() { Provider = "existing" }
            } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        Should.Throw<ArgumentException>(() => graph.RenameConnection("Resources:broker", "renamed"));
        graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); graph.CanRedo.ShouldBeFalse();
    }

    [Fact]
    public void Removal_keeps_missing_references_instead_of_falling_back_and_undo_restores_credentials()
    {
        var graph = StudioTestServices.CreateGraph(); graph.LoadJson(Configuration, "FluxFlow"); graph.ResetHistory();
        var before = graph.ToProjectJson();
        graph.RemoveConnection("Workflows:other:Resources:broker");
        var after = graph.ToProjectJson();
        var reference = JsonNode.Parse(graph.ToJson())!["FluxFlow"]!["Workflows"]!["other"]!["local"]!["Connection"]!.GetValue<string>();
        reference.ShouldBe("other.Resources.broker");
        ConnectionSelection.Resolve(graph.Connections, "mqtt.client", reference, "other").ShouldBeNull();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(after); reopened.ToProjectJson().ShouldBe(after);
    }

    [Fact]
    public void Rename_does_not_capture_previously_shared_references_and_rejects_collisions_without_edits()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"new":{"Type":"mqtt.client"}},"Workflows":{"main":{"Resources":{"old":{"Type":"mqtt.client"},"occupied":{"Type":"mqtt.client"}},"receive":{"Type":"mqtt.subscriber","Connection":"new"}}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        Should.Throw<ArgumentException>(() => graph.RenameConnection("Workflows:main:Resources:old", "occupied"));
        Should.Throw<ArgumentException>(() => graph.RenameConnection("Workflows:main:Resources:old", "invalid:name"));
        graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
        graph.RenameConnection("Workflows:main:Resources:old", "new");
        var reference = JsonNode.Parse(graph.ToJson())!["Workflows"]!["main"]!["receive"]!["Connection"]!.GetValue<string>();
        reference.ShouldBe("Resources.new");
        ConnectionSelection.Resolve(graph.Connections, "mqtt.client", reference, "main")!.Workflow.ShouldBeNull();
    }
}
