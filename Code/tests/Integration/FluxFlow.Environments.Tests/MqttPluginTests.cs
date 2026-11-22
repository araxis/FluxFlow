using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Application.Addressing;
using FluxFlow.Execution;
using FluxFlow.Execution.Plugins;
using FluxFlow.Plugins;
using FluxFlow.Studio.UI;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class MqttPluginTests
{
    [Fact]
    public void Portable_design_exposes_topic_fields_without_loading_a_driver_or_UI()
    {
        var catalog = new FluxFlow.Plugins.Mqtt.MqttPlugin().Description;
        catalog.StudioEntry.ShouldBeNull();
        var entry = catalog.Design.ShouldNotBeNull();
        var assembly = System.Reflection.Assembly.Load(entry.Assembly);
        entry.Dependencies.SelectMany(name => System.Reflection.Assembly.Load(name).GetReferencedAssemblies())
            .Concat(assembly.GetReferencedAssemblies()).ShouldNotContain(a => a.Name == "MQTTnet" || a.Name == "FluxFlow.Components.Mqtt" ||
            a.Name!.Contains("Studio") || a.Name.Contains("AspNetCore.Components") || a.Name.Contains("MudBlazor"));
        var design = (IPluginDesign)Activator.CreateInstance(assembly.GetType(entry.Type, true)!)!;
        var subscriber = catalog.Components.Single(c => c.Palette.ComponentType == "mqtt.subscriber");
        var list = design.Component(subscriber).Collection.ShouldNotBeNull();
        var manual = new Dictionary<string, JsonElement> { ["AcknowledgementMode"] = JsonSerializer.SerializeToElement(1) };
        list.Fields(default, manual).Select(f => f.Name).ShouldBe(["Topic", "QoS"]);
        var row = list.Edit(default, new Dictionary<string, string?> { ["Topic"] = "trial/#", ["QoS"] = "1" });
        list.Validate(row, manual, "Topics");
        list.Write(manual, [row]);
        list.Title(list.Read(manual).Single()).ShouldBe("trial/#");
    }

    private const string Configuration = """
        {"Resources":{"broker":{"Type":"mqtt.client","Host":"localhost","ClientId":"existing","Username":"tester","Password":"test"}},
         "Workflows":{"main":{
           "controller":{"Type":"mqtt.connection","Connection":"broker"},
           "prepare":{"Type":"mapper","Engine":"jsonata","Expression":"$","Output":"send.Input"},
           "send":{"Type":"mqtt.publisher","Connection":"broker","Output":"result.Input"},
           "result":{"Type":"mapper","Engine":"jsonata","Expression":"$"},
           "receive":{"Type":"mqtt.subscriber","Connection":"broker","AcknowledgementMode":"Manual","MaxPendingAcknowledgements":7,
                      "Topics":[{"Topic":"sensor/#","QoS":1}],"ACK":"settle.Output"},
           "settle":{"Type":"mapper","Engine":"jsonata","Expression":"$"}}}}
        """;

    [Fact]
    public void Existing_project_round_trips_through_missing_plugin_and_reinstalled_catalog()
    {
        var plugin = new FluxFlow.Plugins.Mqtt.MqttPlugin();
        using var original = new ServiceCollection().AddStudioUI([plugin.Description, new FluxFlow.Plugins.Transformation.TransformationPlugin().Description]).BuildServiceProvider();
        var graph = new DesignerGraphState(original.GetServices<IComponentDescription>());
        graph.LoadJson(Configuration);
        var saved = graph.ToProjectJson();
        var baseline = JsonNode.Parse(saved);
        var wire = JsonSerializer.Serialize(new[] { plugin.Description }, PluginJsonContext.Default.PluginDescriptionArray);
        foreach (var installed in new[] { true, false, true })
        {
            var catalog = installed ? JsonSerializer.Deserialize(wire, PluginJsonContext.Default.PluginDescriptionArray)! : [];
            var services = new ServiceCollection().AddStudioUI([.. catalog, new FluxFlow.Plugins.Transformation.TransformationPlugin().Description]);
            if (installed) FluxFlow.Studio.UI.Features.Plugins.PortableProperties.Configure(services, catalog.Single(), new FluxFlow.Plugins.Mqtt.MqttDesign(), false);
            using var provider = services.BuildServiceProvider();
            graph = new DesignerGraphState(provider.GetServices<IComponentDescription>());
            graph.LoadProject(saved);
            graph.ResetHistory();
            foreach (var type in new[] { "mqtt.connection", "mqtt.publisher", "mqtt.subscriber" })
                (graph.FindDescription(type) is not null).ShouldBe(installed);
            var connection = provider.GetServices<ConnectionDescription>().SingleOrDefault(c => c.Type == "mqtt.client");
            (connection is not null).ShouldBe(installed);
            if (installed)
            {
                var subscriber = graph.FindDescription("mqtt.subscriber")!;
                subscriber.Documentation.ShouldNotBeNullOrWhiteSpace();
                var settings = new Dictionary<string, JsonElement>();
                subscriber.GetPresentation(settings).Inputs.Single(p => p.Name == "ACK").InactiveReason.ShouldNotBeNull();
                subscriber.GetInspector(settings).Sections.SelectMany(s => s.Options).ShouldNotContain(o => o.Name == "MaxPendingAcknowledgements");
                settings["AcknowledgementMode"] = JsonSerializer.SerializeToElement("Manual");
                subscriber.GetPresentation(settings).Inputs.Single(p => p.Name == "ACK").InactiveReason.ShouldBeNull();
                subscriber.GetInspector(settings).Sections.SelectMany(s => s.Options).ShouldContain(o => o.Name == "MaxPendingAcknowledgements");
                connection!.Create()["ClientId"]!.ToString().ShouldNotBe(connection.Create()["ClientId"]!.ToString());
            }
            saved = graph.ToProjectJson();
            JsonNode.DeepEquals(JsonNode.Parse(saved), baseline).ShouldBeTrue();
            graph.SetPortColorOverrides(new(Output: "#123456"));
            var edited = graph.ToProjectJson();
            graph.Undo();
            graph.ToProjectJson().ShouldBe(saved);
            graph.Redo();
            graph.ToProjectJson().ShouldBe(edited);
        }
    }

    [Fact]
    public void Publisher_wiring_survives_reopen_and_undo_redo()
    {
        using var provider = new ServiceCollection().AddStudioUI([new FluxFlow.Plugins.Mqtt.MqttPlugin().Description,
            new FluxFlow.Plugins.Transformation.TransformationPlugin().Description]).BuildServiceProvider();
        var descriptions = provider.GetServices<IComponentDescription>();
        var graph = new DesignerGraphState(descriptions);
        graph.LoadJson(Configuration);
        var saved = graph.ToProjectJson();
        var reopened = new DesignerGraphState(descriptions);
        reopened.LoadProject(saved);
        reopened.ResetHistory();
        var links = reopened.Diagram.Links.OfType<FlowLinkModel>();
        links.ShouldContain(link => link.Original.Target == PortAddress.Parse("main.send.Input"));
        var result = links.Single(link => link.Original.Source == PortAddress.Parse("main.send.Output"));
        reopened.Diagram.SelectModel(result, true);
        reopened.DeleteSelected();
        reopened.RecordEdit();
        var removed = reopened.ToProjectJson();
        removed.ShouldNotBe(saved);
        reopened.Undo();
        reopened.ToProjectJson().ShouldBe(saved);
        reopened.Redo();
        reopened.ToProjectJson().ShouldBe(removed);
    }

    [Fact]
    public void Companion_only_registers_broker_and_components_when_plugin_is_installed()
    {
        using var absent = new InstalledPlugins(new FluxFlow.Plugins.Transformation.TransformationPlugin());
        using var present = new InstalledPlugins(new FluxFlow.Plugins.Mqtt.MqttPlugin(), new FluxFlow.Plugins.Transformation.TransformationPlugin());
        foreach (var (catalog, enabled) in new[] { (absent, false), (present, true) })
        {
            using var provider = new ServiceCollection().AddLocalFlowRunner(catalog).BuildServiceProvider();
            var adapters = provider.GetServices<FluxFlow.Environments.IResourceEnvironmentAdapter>().ToArray();
            adapters.OfType<FluxFlow.Environments.MqttEnvironmentAdapter>().Count().ShouldBe(enabled ? 1 : 0);
            if (enabled) WorkflowRuntimeCheck.Validate(Configuration, default, plugins: catalog);
            else Should.Throw<InvalidOperationException>(() => WorkflowRuntimeCheck.Validate(Configuration, default, plugins: catalog))
                    .Message.ShouldContain("mqtt.subscriber");
        }
    }
}
