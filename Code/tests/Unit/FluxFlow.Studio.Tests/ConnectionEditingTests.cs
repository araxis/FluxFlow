using System.Text.Json.Nodes;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Connections;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ConnectionEditingTests
{
    [Fact]
    public void Connection_choices_filter_by_type_resolve_scopes_and_do_not_rewrite_saved_references()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"mqtt.client"},"api":{"Type":"http.client"}},"Workflows":{"main":{"Resources":{"broker":{"Type":"http.client"}},"receive":{"Type":"mqtt.subscriber","Connection":"broker"}},"other":{"Resources":{"broker":{"Type":"mqtt.client"}}}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var connections = graph.Connections;
        ConnectionSelection.Compatible(connections, "mqtt.client").Select(item => item.Address)
            .ShouldBe(new[] { "Resources.broker", "other.Resources.broker" });
        ConnectionSelection.Resolve(connections, "mqtt.client", "broker", "main")!.Address.ShouldBe("Resources.broker");
        ConnectionSelection.Resolve(connections, "http.client", "broker", "main")!.Address.ShouldBe("main.Resources.broker");
        ConnectionSelection.Resolve(connections, "mqtt.client", "broker", "other")!.Address.ShouldBe("other.Resources.broker");
        ConnectionSelection.Resolve(connections, "mqtt.client", "Resources.broker", "other")!.Address.ShouldBe("Resources.broker");
        ConnectionSelection.Resolve(connections, "mqtt.client", "Resources.api", "main").ShouldBeNull();
        ConnectionSelection.Resolve(connections, "mqtt.client", "deleted", "main").ShouldBeNull();
        ConnectionSelection.Resolve(connections, "mqtt.client", "", "main").ShouldBeNull();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void Usage_resolution_skips_a_local_resource_of_the_wrong_type_like_keyed_DI()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"service":{"Type":"mqtt.client"}},"Workflows":{"main":{"Resources":{"service":{"Type":"http.client"}},"receive":{"Type":"mqtt.subscriber","Connection":"service"},"send":{"Type":"http.client","Client":"service"}}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        graph.ConnectionUsages(graph.Connections.Single(item => item.Workflow is null)).ShouldBe(["main.receive · Connection"]);
        graph.ConnectionUsages(graph.Connections.Single(item => item.Workflow == "main")).ShouldBe(["main.send · Client"]);
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void Nested_connection_paths_use_existing_resource_resolution_and_preserve_group_casing()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"resources":{"Connections":{"api":{"type":"http.client","BaseAddress":"http://original/"}}},"Workflows":{"main":{"send":{"Type":"http.client","Client":"Connections.api"}}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var resource = graph.Connections.Single();
        resource.Path.ShouldBe("Resources:Connections:api");
        graph.ConnectionUsages(resource).ShouldBe(["main.send · Client"]);
        resource.Settings["BaseAddress"] = "http://updated/";
        graph.SetConnection(resource.Name, resource.Workflow, resource.Settings);
        JsonNode.Parse(graph.ToJson())!["resources"]!["Connections"]!["api"]!["BaseAddress"]!.GetValue<string>().ShouldBe("http://updated/");
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
    }
    private const string Configuration = """
        {"KeepOutside":42,"FluxFlow":{"resources":{"broker":{"type":"mqtt.client","Address":"mqtt://original:1883","ClientId":"KeepCase","Custom":{"Keep":true}}},
        "Workflows":{"main":{"receive":{"Type":"mqtt.subscriber","Connection":"broker"}},
        "other":{"Resources":{"broker":{"Type":"mqtt.client","Address":"mqtt://local:1883"}},
        "local":{"Type":"mqtt.subscriber","Connection":"broker"},"shared":{"Type":"mqtt.subscriber","Connection":"Resources.broker"}}}}}
        """;

    [Fact]
    public void Connection_edits_preserve_configuration_layout_and_environment_as_one_undo_action()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Configuration, "FluxFlow");
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = new() { Provider = "mosquitto" } } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var connection = graph.Connections.Single(item => item.Workflow is null);
        connection.Settings["Address"] = "mqtt://changed:1883";
        graph.ToProjectJson().ShouldBe(before);
        graph.SetConnection(connection.Name, connection.Workflow, connection.Settings);
        var after = graph.ToProjectJson();
        var configuration = JsonNode.Parse(graph.ToJson())!;
        configuration["KeepOutside"]!.GetValue<int>().ShouldBe(42);
        configuration["FluxFlow"]!["resources"]!["broker"]!["type"]!.GetValue<string>().ShouldBe("mqtt.client");
        configuration["FluxFlow"]!["resources"]!["broker"]!["Custom"]!["Keep"]!.GetValue<bool>().ShouldBeTrue();
        graph.GetEnvironment().Resources["Resources:broker"].Provider.ShouldBe("mosquitto");
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(after);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(after);
        reopened.ExportConfiguration().ShouldBe(graph.ExportConfiguration());
    }

    [Fact]
    public void Connection_usages_resolve_local_shadowing_and_explicit_application_references()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Configuration, "FluxFlow");
        graph.ConnectionUsages(graph.Connections.Single(item => item.Workflow is null))
            .ShouldBe(["main.receive · Connection", "other.shared · Connection"]);
        graph.ConnectionUsages(graph.Connections.Single(item => item.Workflow == "other"))
            .ShouldBe(["other.local · Connection"]);
    }

    [Fact]
    public void Adding_a_connection_is_undoable_and_rejected_edits_leave_the_project_unchanged()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Configuration, "FluxFlow");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var settings = JsonNode.Parse("""{"Type":"http.client","BaseAddress":"http://localhost:8080/"}""")!.AsObject();
        graph.SetConnection("api", null, settings, add: true);
        var after = graph.ToProjectJson();
        var revision = graph.Revision;
        Should.Throw<ArgumentException>(() => graph.SetConnection("API", null, settings, add: true));
        Should.Throw<ArgumentException>(() => graph.SetConnection("missing", null, settings));
        Should.Throw<ArgumentException>(() => graph.SetConnection("api", null, new()));
        graph.Revision.ShouldBe(revision);
        graph.ToProjectJson().ShouldBe(after);
        graph.Undo();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(after);
    }
}
