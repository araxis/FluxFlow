using FluxFlow.Application.Addressing;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class RabbitStudioProjectTests
{
    [Fact]
    public void Sample_loads_with_manual_settlement_and_preserves_its_configuration()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadProject(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "rabbitmq.fluxflow")));
        var configuration = graph.ExportConfiguration();
        configuration.ShouldContain("amqp.settlement");
        graph.GetEnvironment().Resources["Resources:broker"].Provider.ShouldBe("rabbitmq-amqp");
        var saved = graph.ToProjectJson();
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(saved);
        reopened.ExportConfiguration().ShouldBe(configuration);
        reopened.ToProjectJson().ShouldBe(saved);
        reopened.ResetHistory();
        var commandWire = reopened.Diagram.Links.OfType<FlowLinkModel>()
            .Single(link => link.Original.Target == PortAddress.Parse("main.acknowledge.Input"));
        reopened.Diagram.SelectModel(commandWire, true);
        reopened.DeleteSelected();
        reopened.RecordEdit();
        var unwired = reopened.ToProjectJson();
        var workflow = System.Text.Json.Nodes.JsonNode.Parse(reopened.ToJson())!["Workflows"]!["main"]!;
        workflow["settlement"]!.AsObject().ContainsKey("Output").ShouldBeFalse();
        workflow["acknowledge"]!["Consumer"]!.GetValue<string>().ShouldBe("consumer");
        reopened.Undo();
        reopened.ToProjectJson().ShouldBe(saved);
        reopened.Redo();
        reopened.ToProjectJson().ShouldBe(unwired);
    }

    [Theory]
    [InlineData(false, ServiceDocumentKind.NameList)]
    [InlineData(true, ServiceDocumentKind.NameList)]
    [InlineData(true, ServiceDocumentKind.DirectiveLines, true)]
    [InlineData(true, ServiceDocumentKind.DirectiveLines)]
    [InlineData(false, ServiceDocumentKind.KeyValueLines)]
    [InlineData(true, ServiceDocumentKind.KeyValueLines)]
    [InlineData(false, ServiceDocumentKind.ObjectCollections)]
    [InlineData(true, ServiceDocumentKind.ObjectCollections)]
    public void Broker_startup_file_edit_round_trips_through_project_history(bool mqtt, ServiceDocumentKind format, bool acl = false)
    {
        var provider = format == ServiceDocumentKind.DirectiveLines ? FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider :
            mqtt ? FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider : FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider;
        var file = provider.StartupFiles.Single(f => f.Format == format && (f.Directives is not null) == acl);
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(System.Text.Json.JsonSerializer.Serialize(new { Resources = new { broker = new { Type = provider.ResourceType } }, Workflows = new { main = new { } } }));
        var original = format switch
        {
            ServiceDocumentKind.DirectiveLines when acl => "# anonymous\r\ntopic read public/#\nuser first\r\ntopic write local/+",
            ServiceDocumentKind.DirectiveLines => "\uFEFF# local overrides\r\nplugin_opt_label  first  \n# EOF",
            ServiceDocumentKind.NameList => " [ custom_plugin ] .\r\n",
            ServiceDocumentKind.ObjectCollections => """{ "queues":[{"name":"first","arguments":{"nested":[1,true,null]}}], "custom":{"keep":42} }""",
            _ => "\uFEFF# local overrides\r\n\tcustom.key  = first  \n# EOF"
        };
        var binding = new ResourceBinding { Provider = provider.Name, Files = new() { [file.Path] = original } };
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = binding } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var setup = graph.GetEnvironment();
        var draft = new FluxFlow.Studio.UI.Features.Environments.Services.Documents.ServiceDocumentDraft(
            file.Format!.Value, binding.Files[file.Path], file.Templates, file.Names, file.Directives);
        draft.Serialize().ShouldBe(original);
        if (draft.Collections is { } collections)
            collections.Schema.Fields("queues", collections.Rows("queues").Single()).OfType<DocumentText>().Single(f => f.Label == "Name").Write("second");
        else if (format == ServiceDocumentKind.NameList) draft.Entries.Add(new() { Name = "rabbitmq_management" });
        else if (acl) file.Directives!.Fields(draft.Entries[1]).OfType<DocumentText>().Single().Write("second");
        else draft.Entries.Single().Value = "second";
        setup.Resources["Resources:broker"].Files[file.Path] = draft.Serialize();
        graph.SetEnvironment(setup);
        var saved = graph.ToProjectJson();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
        var reopened = StudioTestServices.CreateGraph();
        reopened.LoadProject(saved);
        var reopenedFile = reopened.GetEnvironment().Resources["Resources:broker"].Files[file.Path];
        if (format == ServiceDocumentKind.ObjectCollections)
        {
            var expected = System.Text.Json.Nodes.JsonNode.Parse(original.Replace("first", "second"));
            System.Text.Json.Nodes.JsonNode.DeepEquals(expected, System.Text.Json.Nodes.JsonNode.Parse(reopenedFile)).ShouldBeTrue();
        }
        else reopenedFile.ShouldBe(format == ServiceDocumentKind.NameList ? "[custom_plugin,rabbitmq_management].\r\n" : original.Replace("first", "second"));
    }

    [Fact]
    public void Locked_amqp_profile_keeps_files_and_secret_references_through_undo_redo_and_reopen()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"amqp.connection","Endpoint":"amqp://original"}},"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var originalConfiguration = graph.ExportConfiguration();
        var registry = new ImageRegistry { Address = "docker.io", Name = "Docker Hub" };
        var profile = new ContainerProfile { Name = "AMQP", Provider = "rabbitmq-amqp", RegistryId = registry.Id, SetupLocked = true, Settings = new()
        {
            Image = "library/rabbitmq:4.1.0-management", Files = new() { ["/etc/rabbitmq/enabled_plugins"] = "[rabbitmq_management]." },
            Variables = new() { ["RABBITMQ_DEFAULT_USER_ENVIRONMENT_VARIABLE"] = "BROKER_USER", ["RABBITMQ_DEFAULT_PASS_ENVIRONMENT_VARIABLE"] = "BROKER_PASSWORD" }
        } };
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = profile.CreateBinding([registry]) } });
        var saved = graph.ToProjectJson();
        profile.Settings.Variables.Clear();
        profile.Settings.Files.Clear();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(saved);
        var binding = reopened.GetEnvironment().Resources["Resources:broker"];
        binding.ValidateSnapshot();
        binding.Profile!.SetupLocked.ShouldBeTrue();
        binding.Variables["RABBITMQ_DEFAULT_PASS_ENVIRONMENT_VARIABLE"].ShouldBe("BROKER_PASSWORD");
        binding.Files["/etc/rabbitmq/enabled_plugins"].ShouldBe("[rabbitmq_management].");
        Should.Throw<ArgumentException>(() => (binding with { Image = "changed:latest" }).ValidateSnapshot());
        reopened.ExportConfiguration().ShouldBe(originalConfiguration);
    }

    [Fact]
    public void Existing_amqp_catalog_binding_is_compatible_and_isolated_from_catalog_edits()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"amqp.connection","Endpoint":"amqp://original"}},"Workflows":{"main":{}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var host = new InfrastructureHost { Name = "AMQP server", Services = [new() { Name = "RabbitMQ", Type = "amqp.connection", Endpoint = "amqps://broker.example:5671" }] };
        host.Validate();
        var topology = graph.GetEnvironmentDefinition().Topology;
        topology.PlaceHost(host, 100, 100);
        var service = topology.Nodes.Single(item => item.ExistingService != null);
        topology.Nodes.Add(new() { Id = "app", Name = "Application", Kind = EnvironmentNodeKind.Application, Bindings = new() { ["Resources:broker"] = service.Id } });
        graph.SetTopology(topology);
        graph.EnvironmentLimitation().ShouldBeNull();
        var saved = graph.ToProjectJson();
        host.Services.Clear();
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(saved);
        var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(saved);
        reopened.CreateEnvironmentRunRequest().Instances.Single().ExistingServices["Resources:broker"].Endpoint.ShouldBe("amqps://broker.example:5671");
        var changed = reopened.GetEnvironmentDefinition().Topology;
        var changedService = changed.Nodes.Single(item => item.ExistingService != null);
        changed.Nodes[changed.Nodes.IndexOf(changedService)] = changedService with { ExistingService = changedService.ExistingService! with { Endpoint = "amqp://changed" } };
        Should.Throw<ArgumentException>(() => reopened.SetTopology(changed));
        reopened.ToProjectJson().ShouldBe(saved);
    }
}
