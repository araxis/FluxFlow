using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using FluxFlow.Plugins.RabbitMQ;
using FluxFlow.Studio.UI.Features.Plugins;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class RabbitComponentDraftTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Draft:")]
    [InlineData("Workflows:orders:receive:")]
    public void Portable_validator_accepts_standalone_or_nested_settings_and_still_checks_feedback_links(string prefix)
    {
        using var document = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [prefix + "Connection"] = "broker", [prefix + "Queue"] = "orders",
            [prefix + "Topology:Queues:0:Name"] = "orders",
            [prefix + "ACK:0:Address"] = "finished.Output", [prefix + "NACK"] = "failed.Output"
        }).Build();
        IConfiguration settings = prefix.Length == 0 ? document : document.GetSection(prefix.TrimEnd(':'));
        var consumer = new RabbitMQPlugin().Description.Components.Single(item => item.Palette.ComponentType == "amqp.consumer");
        var validate = new RabbitDesign().Component(consumer).Validate.ShouldNotBeNull();
        validate(settings);
        settings["Topology:Queues:0:Name"] = "";
        Should.Throw<ArgumentException>(() => validate(settings));
        settings["Topology:Queues:0:Name"] = "orders";
        settings["ACK:0:Unexpected"] = "invalid";
        Should.Throw<JsonException>(() => validate(settings)).Message.ShouldContain("Unexpected");
    }

    [Theory]
    [InlineData("""{"Type":"amqp.publisher","Connection":"broker"}""", true)]
    [InlineData("""{"Type":"amqp.publisher","Connection":"broker","Topology":{"Queues":[]}}""", true)]
    [InlineData("""{"Type":"amqp.consumer","Connection":"broker","Queue":"orders","ACK":"work.Output","Output":"log.Input","Future":{"Nested":[1,2]},"Topology":{"Queues":[{"Name":"orders","Arguments":{"ttl":{"Type":"Int32","Value":1000}}}]}}""", true)]
    [InlineData("""{"Type":"amqp.consumer","Queue":"orders"}""", false)]
    public void Shared_editor_validation_preserves_the_entire_draft(string json, bool valid)
    {
        var value = JsonNode.Parse(json)!.AsObject();
        var component = new RabbitMQPlugin().Description.Components.Single(c => c.Palette.ComponentType == (string?)value["Type"]);
        var rules = new RabbitDesign().Component(component);
        var original = value.ToJsonString();
        if (valid) PortableProperties.Validate(rules, value, component: true);
        else Should.Throw<ArgumentException>(() => PortableProperties.Validate(rules, value, component: true));
        value.ToJsonString().ShouldBe(original);
    }
}
