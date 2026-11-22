using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Configuration.Components.Nats;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class BrokerConnectionInspectorTests
{
    [Theory]
    [InlineData("amqp", "amqps://broker.example", true)]
    [InlineData("amqp", "amqp://broker.example/wrong-place", false)]
    [InlineData("nats", "tls://broker.example:4222", true)]
    [InlineData("nats", "nats://user@broker.example", false)]
    public void Endpoint_field_and_advanced_JSON_use_the_registered_protocol_rules(string family, string endpoint, bool accepted)
    {
        var editor = family == "amqp" ? StudioTestServices.RabbitConnection() : StudioTestServices.NatsConnection();
        var settings = editor.Create(); settings["Endpoint"] = endpoint;
        var field = editor.PropertyGroups!(settings).SelectMany(group => group.Options).Single(option => option.Name == "Endpoint");
        field.ValidateText!(null).ShouldBeNull();
        (field.ValidateText(endpoint) is null).ShouldBe(accepted);
        if (accepted) Should.NotThrow(() => editor.Validate!(settings)); else Should.Throw<ArgumentException>(() => editor.Validate!(settings));
    }

    [Fact]
    public void Nats_alternative_credentials_are_repairable_and_never_resolved_by_the_editor()
    {
        var editor = StudioTestServices.NatsConnection();
        var settings = editor.Create();
        var auth = editor.PropertyGroups!(settings).Single(group => group.Title == "Credential variables");
        string[] Visible() => auth.Options.Where(option => auth.IsVisible!(Fields(settings), option.Name)).Select(option => option.Name).ToArray();
        settings["tokenEnvironmentVariable"] = "TEST_TOKEN_REFERENCE";
        Visible().ShouldBe(["TokenEnvironmentVariable"]);
        Should.NotThrow(() => editor.Validate!(settings));
        settings["passWordEnvironmentVariable"] = "TEST_PASSWORD_REFERENCE";
        Visible().ShouldBe(["UsernameEnvironmentVariable", "PasswordEnvironmentVariable", "TokenEnvironmentVariable"]);
        Should.Throw<ArgumentException>(() => editor.Validate!(settings));
        settings.Remove("tokenEnvironmentVariable"); settings["UsernameEnvironmentVariable"] = "TEST_USER_REFERENCE";
        Should.NotThrow(() => editor.Validate!(settings));
        var validate = auth.Options[0].ValidateText!;
        validate("").ShouldBeNull(); validate("NAME=value").ShouldNotBeNull();
    }

    [Fact]
    public void Disabled_TLS_keeps_saved_certificate_references_reachable_even_for_numeric_modes()
    {
        var editor = StudioTestServices.NatsConnection();
        var settings = editor.Create(); settings["tlsmode"] = (int)NatsTlsMode.Disable;
        var tls = editor.PropertyGroups!(settings).Single(group => group.Title == "TLS");
        tls.IsVisible!(Fields(settings), "CaFileEnvironmentVariable").ShouldBeFalse();
        settings["caFileEnvironmentVariable"] = "TRUST_REFERENCE";
        tls.IsVisible(Fields(settings), "CaFileEnvironmentVariable").ShouldBeTrue();
        Should.Throw<ArgumentException>(() => editor.Validate!(settings));
        settings.Remove("caFileEnvironmentVariable");
        Should.NotThrow(() => editor.Validate!(settings));
    }

    private static Dictionary<string, JsonElement> Fields(JsonObject settings) =>
        settings.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value));
}
