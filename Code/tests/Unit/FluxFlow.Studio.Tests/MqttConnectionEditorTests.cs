using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Connections.Effective;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class MqttConnectionEditorTests
{
    [Theory]
    [InlineData("mqtt://host", true)]
    [InlineData("tcp://host:1883", true)]
    [InlineData("wss://host/with/path?x=1", true)]
    [InlineData("http://host", false)]
    [InlineData("mqtt://host/with/path", false)]
    [InlineData("mqtts://user@host", false)]
    [InlineData("mqtts://host:0", false)]
    public void Address_field_and_whole_connection_JSON_agree_without_opening_a_connection(string address, bool valid)
    {
        var description = StudioTestServices.MqttConnection();
        var settings = description.Create();
        settings["Address"] = address;
        var field = description.PropertyGroups!(settings).SelectMany(group => group.Options).Single(option => option.Name == "Address");
        (field.ValidateText!(address) is null).ShouldBe(valid);
        if (valid) Should.NotThrow(() => description.Validate!(settings)); else Should.Throw<ArgumentException>(() => description.Validate!(settings));
    }

    [Theory]
    [InlineData("Delay", "1.00:00:00", true)]
    [InlineData("Delay", "1.00:00:01", false)]
    [InlineData("MaxDelay", "1.00:00:00", true)]
    [InlineData("MaxDelay", "1.00:00:01", false)]
    public void Reconnect_duration_editor_matches_the_registered_retry_policy(string name, string value, bool valid)
    {
        var description = StudioTestServices.MqttConnection();
        var settings = description.Create(); settings["Reconnect"] = new JsonObject { [name] = value };
        var field = description.PropertyGroups!(settings).Single(group => group.Path == "Reconnect").Options.Single(option => option.Name == name);
        var duration = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        (duration >= field.MinDuration && duration <= field.MaxDuration).ShouldBe(valid);
        if (valid) Should.NotThrow(() => description.Validate!(settings)); else Should.Throw<System.ComponentModel.DataAnnotations.ValidationException>(() => description.Validate!(settings));
    }

    [Theory]
    [InlineData("port", "\"not-a-port\"")]
    [InlineData("Port", "999999999999")]
    [InlineData("Tls", "\"not-a-boolean\"")]
    public void Invalid_legacy_target_does_not_display_a_guessed_effective_endpoint(string key, string malformed)
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(EffectiveConnectionSettingsTests.Configuration);
        var resource = graph.Connections.Single();
        resource.Settings[FluxFlow.Environments.ResourceJson.Key(resource.Settings, key)] = JsonNode.Parse(malformed);
        var view = EffectiveConnectionSettings.Resolve(resource, graph.GetEnvironmentDefinition(), [resource], "Address", StudioTestServices.MqttConnection(), new FluxFlow.Environments.MqttEnvironmentAdapter()).Single();
        view.Fields.ShouldBeEmpty(); view.Notice.ShouldNotBeNull();
    }
}
