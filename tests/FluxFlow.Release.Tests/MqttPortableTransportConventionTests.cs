using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class MqttPortableTransportConventionTests
{
    [Fact]
    public void Portable_mqtt_transport_contract_has_no_provider_reflection_or_browser_policy()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var core = Path.Combine(root, "src", "Mqtt", "FluxFlow.Components.Mqtt");
        var composition = Path.Combine(
            root,
            "src",
            "Mqtt",
            "FluxFlow.Components.Mqtt.Composition");
        var enumSource = File.ReadAllText(Path.Combine(
            core,
            "Configuration",
            "MqttBrokerTransport.cs"));
        var brokerSource = File.ReadAllText(Path.Combine(
            core,
            "Configuration",
            "MqttBrokerConfiguration.cs"));
        var validationSource = File.ReadAllText(Path.Combine(
            core,
            "Client",
            "MqttClientConfigurationValidator.cs"));
        var authoringSource = File.ReadAllText(Path.Combine(
            composition,
            "Authoring",
            "MqttResourceAuthoring.cs"));
        var definitionSource = File.ReadAllText(Path.Combine(
            composition,
            "MqttComponentDefinition.cs"));
        var registrarSource = File.ReadAllText(Path.Combine(
            composition,
            "MqttCompositionResourceRegistrar.cs"));

        enumSource.ShouldContain("public enum MqttBrokerTransport");
        CountOccurrences(enumSource, "Tcp").ShouldBe(1);
        CountOccurrences(enumSource, "WebSocket").ShouldBe(1);
        brokerSource.ShouldContain("public MqttBrokerTransport Transport { get; init; }");
        brokerSource.ShouldContain("public bool UseTls { get; init; }");
        brokerSource.ShouldContain("public string WebSocketPath { get; init; } = \"/mqtt\";");
        authoringSource.ShouldContain("public MqttBrokerTransport? Transport { get; set; }");
        authoringSource.ShouldContain("public string? WebSocketPath { get; set; }");
        authoringSource.ShouldContain("transport.ToString()");
        definitionSource.ShouldContain("public const string Transport = \"Transport\";");
        definitionSource.ShouldContain(
            "public const string WebSocketPath = \"WebSocketPath\";");
        registrarSource.ShouldContain("\"Transport\",");
        registrarSource.ShouldContain("\"WebSocketPath\"");
        validationSource.ShouldContain("Enum.IsDefined(configuration.Broker.Transport)");
        validationSource.ShouldContain("broker.WebSocketPath[0] != '/'");
        validationSource.ShouldContain("broker.WebSocketPath.Contains('?')");
        validationSource.ShouldContain("broker.WebSocketPath.Contains('#')");
        validationSource.ShouldContain(
            "MQTT WebSocketPath can only be customized for WebSocket transport.");
        validationSource.ShouldContain("!string.IsNullOrWhiteSpace(broker.ServerName)");

        var neutralSource = string.Join(
            Environment.NewLine,
            enumSource,
            brokerSource,
            validationSource,
            authoringSource,
            definitionSource,
            registrarSource);
        foreach (var forbidden in new[]
        {
            "MQTTnet",
            "Pulse.Mqtt",
            "System.Reflection",
            "Assembly.Load",
            "GetTypes(",
            "OperatingSystem.IsBrowser",
            "RuntimeInformation",
            "BrowserWebSocket",
            "#if BROWSER"
        })
        {
            neutralSource.ShouldNotContain(forbidden);
        }

        var documentation = new[]
        {
            Path.Combine(core, "README.md"),
            Path.Combine(composition, "README.md"),
            Path.Combine(root, "docs", "21-component-type-names.md")
        };
        foreach (var path in documentation)
        {
            var content = File.ReadAllText(path);
            content.ShouldContain("Tcp");
            content.ShouldContain("WebSocket");
            content.ShouldContain("UseTls");
            content.ShouldContain("ws");
            content.ShouldContain("wss");
            content.ShouldContain("/mqtt");
        }
        var coreReadme = File.ReadAllText(Path.Combine(core, "README.md"));
        coreReadme.ShouldContain("| `Tcp` | `false` | TCP |");
        coreReadme.ShouldContain("| `Tcp` | `true` | TLS over TCP |");
        coreReadme.ShouldContain("| `WebSocket` | `false` | `ws` |");
        coreReadme.ShouldContain("| `WebSocket` | `true` | `wss` |");
        coreReadme.ShouldContain("the neutral MQTT core does not detect host platforms");
        var compositionReadme = File.ReadAllText(Path.Combine(composition, "README.md"));
        compositionReadme.ShouldContain("| omitted or `Tcp` | `false` | TCP |");
        compositionReadme.ShouldContain("| omitted or `Tcp` | `true` | TLS over TCP |");
        compositionReadme.ShouldContain("| `WebSocket` | `false` | `ws` |");
        compositionReadme.ShouldContain("| `WebSocket` | `true` | `wss` |");

        var sampleDirectory = Path.Combine(
            root,
            "samples",
            "FluxFlow.MqttCompositionSample");
        var sampleProgram = File.ReadAllText(Path.Combine(sampleDirectory, "Program.cs"));
        sampleProgram.ShouldContain(
            "broker.Transport = MqttBrokerTransport.WebSocket;");
        sampleProgram.ShouldContain("broker.UseTls = true;");
        sampleProgram.ShouldContain("broker.WebSocketPath = \"/mqtt\";");
        var sampleConfiguration = File.ReadAllText(Path.Combine(
            sampleDirectory,
            "appsettings.json"));
        sampleConfiguration.ShouldContain("\"Transport\": \"WebSocket\"");
        sampleConfiguration.ShouldContain("\"UseTls\": true");
        sampleConfiguration.ShouldContain("\"WebSocketPath\": \"/mqtt\"");
        File.ReadAllText(Path.Combine(sampleDirectory, "README.md"))
            .ShouldContain("portable `WebSocket` + `UseTls` broker shape for WSS");
    }

    [Fact]
    public void Pulse_websocket_transport_dependency_is_exact_adapter_local_and_uses_raw_client()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var mqttRoot = Path.Combine(root, "src", "Mqtt");
        var centralPackages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        var webSocketVersions = centralPackages
            .Descendants("PackageVersion")
            .Where(static element =>
                (string?)element.Attribute("Include") == "Pulse.Mqtt.Transport.WebSocket")
            .Select(static element => (string?)element.Attribute("Version"))
            .ToArray();
        webSocketVersions.ShouldBe(["2.29.0"]);

        var projects = Directory.GetFiles(
            mqttRoot,
            "*.csproj",
            SearchOption.AllDirectories);
        var webSocketReferences = projects
            .Where(path => XDocument.Load(path)
                .Descendants("PackageReference")
                .Any(static element =>
                    (string?)element.Attribute("Include") ==
                    "Pulse.Mqtt.Transport.WebSocket"))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();
        webSocketReferences.ShouldBe([
            Path.Combine(
                "src",
                "Mqtt",
                "FluxFlow.Components.Mqtt.PulseMqtt",
                "FluxFlow.Components.Mqtt.PulseMqtt.csproj")
        ]);

        var expectedVersions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FluxFlow.Components.Mqtt"] = "7.1.0",
            ["FluxFlow.Components.Mqtt.Composition"] = "7.1.0-rc.1",
            ["FluxFlow.Components.Mqtt.MqttNet"] = "3.1.0",
            ["FluxFlow.Components.Mqtt.PulseMqtt"] = "4.1.0"
        };
        foreach (var (projectName, expectedVersion) in expectedVersions)
        {
            var project = XDocument.Load(Path.Combine(
                mqttRoot,
                projectName,
                $"{projectName}.csproj"));
            project.Descendants("Version")
                .Select(static element => element.Value)
                .ShouldHaveSingleItem()
                .ShouldBe(expectedVersion);
        }

        var pulseDirectory = Path.Combine(
            mqttRoot,
            "FluxFlow.Components.Mqtt.PulseMqtt");
        var pulseSource = File.ReadAllText(Path.Combine(
            pulseDirectory,
            "PulseMqttTransportSession.cs"));
        CountOccurrences(pulseSource, "new RawMqttClient(").ShouldBe(1);
        pulseSource.ShouldContain("CreateTcpOptions(broker, certificates)");
        pulseSource.ShouldContain("CreateWebSocketOptions(broker, certificates)");
        pulseSource.ShouldContain("SubProtocol = \"mqtt\"");
        pulseSource.ShouldNotContain("ResilientMqttClient");
        pulseSource.ShouldNotContain("AddPulseMqttClient");
        pulseSource.ShouldNotContain("MqttClientController");
        pulseSource.ShouldNotContain("RetryPlanner");
        pulseSource.ShouldNotContain("OperatingSystem.IsBrowser");

        foreach (var adapterReadme in new[]
        {
            Path.Combine(pulseDirectory, "README.md"),
            Path.Combine(mqttRoot, "FluxFlow.Components.Mqtt.MqttNet", "README.md")
        })
        {
            var content = File.ReadAllText(adapterReadme);
            content.ShouldContain("WebSocket");
            content.ShouldContain("WSS", Case.Insensitive);
            content.ShouldContain("host capability checks belong to the browser host");
        }
    }

    private static int CountOccurrences(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;
}
