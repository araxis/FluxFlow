using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Execution;
using FluxFlow.Execution.Plugins;
using FluxFlow.Plugins;
using FluxFlow.Studio.UI;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class RabbitPluginTests
{
    [Fact]
    public void Portable_design_supplies_declarations_and_feedback_ports_without_a_driver_or_UI()
    {
        var catalog = new FluxFlow.Plugins.RabbitMQ.RabbitMQPlugin().Description;
        catalog.StudioEntry.ShouldBeNull();
        var entry = catalog.Design.ShouldNotBeNull();
        var assembly = System.Reflection.Assembly.Load(entry.Assembly);
        entry.Dependencies.SelectMany(name => System.Reflection.Assembly.Load(name).GetReferencedAssemblies())
            .Concat(assembly.GetReferencedAssemblies()).ShouldNotContain(a => a.Name == "RabbitMQ.Client" ||
                a.Name == "FluxFlow.Components.RabbitMQ" || a.Name!.Contains("Studio") ||
                a.Name.Contains("AspNetCore.Components") || a.Name.Contains("MudBlazor"));
        var design = (IPluginDesign)Activator.CreateInstance(assembly.GetType(entry.Type, true)!)!;
        var consumer = catalog.Components.Single(c => c.Palette.ComponentType == "amqp.consumer");
        consumer.Palette.Inputs.Select(port => port.Name).ShouldBe(["Input", "ACK", "NACK"]);
        var groups = design.Component(consumer).ObjectGroups.ShouldNotBeNull();
        groups.Lists.Select(list => list.Name).ShouldBe(["Exchanges", "Queues", "Bindings"]);
        var argument = groups.Map!.Open("ttl", null);
        argument.ChangeType("Int32"); argument.Text = "1200";
        argument.Save()["Value"]!.GetValue<long>().ShouldBe(1200);
    }

    [Fact]
    public void Removing_and_restoring_the_catalog_preserves_the_existing_project_and_its_editors()
    {
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rabbitmq.fluxflow"));
        var saved = original;
        JsonNode? baseline = null;
        using var present = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.RabbitMQ.RabbitMQPlugin());
        using var absent = RunTestPlugins.WithBasicComponents();
        foreach (var plugins in new[] { present, absent, present })
        {
            var installed = plugins == present;
            var json = JsonSerializer.Serialize(plugins.Descriptions, PluginJsonContext.Default.PluginDescriptionArray);
            var catalog = JsonSerializer.Deserialize(json, PluginJsonContext.Default.PluginDescriptionArray)!;
            var registrations = new ServiceCollection().AddStudioUI(catalog);
            if (installed) FluxFlow.Studio.UI.Features.Plugins.PortableProperties.Configure(registrations, catalog.Single(p => p.Id == "FluxFlow.Plugins.RabbitMQ"), new FluxFlow.Plugins.RabbitMQ.RabbitDesign(), inProcess: false);
            using var studio = registrations.BuildServiceProvider();
            var graph = new DesignerGraphState(studio.GetServices<IComponentDescription>());
            graph.LoadProject(saved);
            graph.ResetHistory();
            foreach (var type in new[] { "amqp.publisher", "amqp.consumer", "amqp.settlement" })
                (graph.FindDescription(type) != null).ShouldBe(installed);
            var broker = studio.GetServices<ConnectionDescription>().SingleOrDefault(d => d.Type == "amqp.connection");
            (broker != null).ShouldBe(installed);
            studio.GetServices<IComponentConfigurator>().Count(c => c.Component.StartsWith("amqp.")).ShouldBe(installed ? 2 : 0);
            if (installed)
            {
                broker!.PropertyGroups.ShouldNotBeNull();
                var consumer = graph.FindDescription("amqp.consumer")!;
                consumer.Documentation.ShouldNotBeNullOrWhiteSpace();
                studio.GetRequiredKeyedService<PropertyRules>("amqp.consumer").TextValidators["Queue"]("").ShouldNotBeNull();
            }
            using var host = new ServiceCollection().AddLocalFlowRunner(plugins).BuildServiceProvider();
            var adapters = host.GetServices<FluxFlow.Environments.IResourceEnvironmentAdapter>().ToArray();
            adapters.Count(a => a.Type == "amqp.connection").ShouldBe(installed ? 1 : 0);
            if (installed) WorkflowRuntimeCheck.Validate(graph.ExportConfiguration(), default, plugins: plugins);
            else Should.Throw<InvalidOperationException>(() =>
                WorkflowRuntimeCheck.Validate(graph.ExportConfiguration(), default, plugins: plugins)).Message.ShouldContain("amqp.");
            saved = graph.ToProjectJson();
            JsonNode.DeepEquals(JsonNode.Parse(saved)!["Configuration"], JsonNode.Parse(original)!["Configuration"]).ShouldBeTrue();
            baseline ??= JsonNode.Parse(saved);
            JsonNode.DeepEquals(JsonNode.Parse(saved), baseline).ShouldBeTrue();
            graph.SetPortColorOverrides(new(Output: "#123456"));
            var edited = graph.ToProjectJson();
            graph.Undo(); JsonNode.DeepEquals(JsonNode.Parse(graph.ToProjectJson()), baseline).ShouldBeTrue();
            graph.Redo(); JsonNode.DeepEquals(JsonNode.Parse(graph.ToProjectJson()), JsonNode.Parse(edited)).ShouldBeTrue();
        }
    }
}
