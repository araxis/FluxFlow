using FluxFlow.Studio.UI;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Authoring.Definitions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ComponentDescriptionTests
{
    [Fact]
    public void No_broker_catalog_registers_no_broker_components_connections_or_editors()
    {
        using var provider = new ServiceCollection().AddStudioUI().BuildServiceProvider();
        var components = provider.GetServices<IComponentDescription>().Select(item => item.Palette.ComponentType);
        var connections = provider.GetServices<FluxFlow.Studio.UI.Features.Connections.ConnectionDescription>().Select(item => item.Type);
        var editors = provider.GetServices<FluxFlow.Studio.UI.Features.Designer.Components.IComponentConfigurator>().Select(item => item.Component);
        components.Concat(connections).Concat(editors).ShouldNotContain(type =>
            type.StartsWith("mqtt.", StringComparison.Ordinal) || type.StartsWith("amqp.", StringComparison.Ordinal) || type.StartsWith("nats.", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_registered_component_has_embedded_markdown_available_without_a_UI_host()
    {
        using var provider = new ServiceCollection().AddCatalogComponents(StudioTestServices.InstalledCatalog).BuildServiceProvider();
        var descriptions = provider.GetServices<IComponentDescription>().ToArray();
        foreach (var description in descriptions)
        {
            description.Documentation.ShouldStartWith("# ");
            description.Documentation.ShouldContain($"`{description.Palette.ComponentType}`");
            description.Documentation.ShouldContain("## Ports");
        }
        var controller = descriptions.Single(item => item.Palette.ComponentType == "mqtt.connection");
        controller.Palette.DisplayName.ShouldBe("MQTT Connection Controller");
        controller.Documentation.ShouldContain("Defining the resource alone does not start network activity.");
        controller.Documentation.ShouldContain("DisableReconnect");
        ComponentDocumentation.Read(typeof(ComponentDescriptionTests)).ShouldBeEmpty();
    }

    [Fact]
    public void Property_descriptions_explain_settings_and_match_registration_defaults()
    {
        using var provider = new ServiceCollection().AddCatalogComponents(StudioTestServices.InstalledCatalog).BuildServiceProvider();
        var configurations = new Dictionary<string, object>
        {
            ["batch"] = new FluxFlow.Configuration.Components.Batching.BatchConfiguration(),
            ["gate"] = new FluxFlow.Configuration.Components.Routing.GateConfiguration(),
            ["mapper"] = new FluxFlow.Configuration.Components.Mapping.MapperConfiguration(),
            ["scheduler"] = new FluxFlow.Configuration.Components.Scheduling.SchedulerConfiguration(),
            ["source"] = new FluxFlow.Configuration.Components.Sources.SourceConfiguration(),
            ["changeFilter"] = new FluxFlow.Configuration.Components.Filtering.ChangeFilterConfiguration(),
            ["deduplicate"] = new FluxFlow.Configuration.Components.Filtering.DeduplicateConfiguration(),
            ["dataGenerator"] = new FluxFlow.Configuration.Components.DataGeneration.DataGeneratorConfiguration(),
            ["rateLimit"] = new FluxFlow.Configuration.Components.RateLimiting.RateLimitConfiguration(),
            ["requestReply"] = new FluxFlow.Configuration.Components.RequestReply.RequestReplyConfiguration(),
            ["reducer"] = new FluxFlow.Configuration.Components.Reducing.ReducerConfiguration(),
            ["retry"] = new FluxFlow.Configuration.Components.Resilience.RetryConfiguration(),
            ["router"] = new FluxFlow.Configuration.Components.Routing.RouterConfiguration(),
            ["http.client"] = new FluxFlow.Configuration.Components.Http.HttpComponentConfiguration(),
            ["logger"] = new FluxFlow.Configuration.Components.Logging.LoggerConfiguration(),
            ["mqtt.connection"] = new FluxFlow.Configuration.Components.Mqtt.MqttComponentConfiguration(),
            ["mqtt.publisher"] = new FluxFlow.Configuration.Components.Mqtt.MqttComponentConfiguration(),
            ["mqtt.subscriber"] = new FluxFlow.Configuration.Components.Mqtt.MqttSubscriberConfiguration(),
            ["serial.text"] = new FluxFlow.Configuration.Components.Serial.SerialTextConfiguration(),
            ["serial.reader"] = new FluxFlow.Configuration.Components.Serial.SerialFrameConfiguration(),
            ["serial.writer"] = new FluxFlow.Configuration.Components.Serial.SerialFrameConfiguration(),
            ["amqp.publisher"] = new FluxFlow.Configuration.Components.RabbitMQ.RabbitComponentConfiguration(),
            ["amqp.consumer"] = new FluxFlow.Configuration.Components.RabbitMQ.RabbitComponentConfiguration(),
            ["amqp.settlement"] = new FluxFlow.Configuration.Components.RabbitMQ.RabbitSettlementConfiguration(),
            ["nats.publisher"] = new FluxFlow.Configuration.Components.Nats.NatsCommandConfiguration(),
            ["nats.subscriber"] = new FluxFlow.Configuration.Components.Nats.NatsSubscriberConfiguration(),
            ["nats.request"] = new FluxFlow.Configuration.Components.Nats.NatsCommandConfiguration(),
            ["nats.reply"] = new FluxFlow.Configuration.Components.Nats.NatsCommandConfiguration(),
            ["nats.jetstream.publisher"] = new FluxFlow.Configuration.Components.Nats.JetStreamConfiguration(),
            ["nats.jetstream.consumer"] = new FluxFlow.Configuration.Components.Nats.JetStreamConfiguration(),
            ["nats.jetstream.settlement"] = new FluxFlow.Configuration.Components.Nats.JetStreamSettlementConfiguration(),
            ["ble.client"] = new FluxFlow.Configuration.Components.Bluetooth.BleClientConfiguration(),
            ["ble.notifications"] = new FluxFlow.Configuration.Components.Bluetooth.BleClientConfiguration(),
            ["ble.server"] = new FluxFlow.Configuration.Components.Bluetooth.BleServerConfiguration(),
            ["ros.publisher"] = new FluxFlow.Configuration.Components.Ros2.RosPublisherConfiguration(),
            ["ros.subscriber"] = new FluxFlow.Configuration.Components.Ros2.RosSubscriberConfiguration(),
            ["http.trigger"] = new FluxFlow.Hosting.AspNetCore.HttpTriggerConfiguration()
        };
        foreach (var description in provider.GetServices<IComponentDescription>())
        {
            var configuration = configurations[description.Palette.ComponentType];
            var options = description.Inspector.Sections.SelectMany(section => section.Options).ToArray();
            var type = description.Palette.ComponentType;
            // These DTOs are shared between operations; only applicable settings belong in each inspector.
            string[] excluded = type switch
            {
                "amqp.publisher" => ["Queue", "Prefetch", "ACK", "NACK", "RequeueOnNack"],
                "amqp.consumer" => ["TimeoutSeconds"],
                "serial.reader" => ["Input"],
                "nats.subscriber" => ["Input"],
                "nats.jetstream.consumer" => ["Input"],
                "nats.jetstream.publisher" => ["Consumer", "CreateConsumer", "FilterSubject", "DeliverPolicy", "AckWaitSeconds", "MaxDeliver", "MaxInFlight"],
                "ble.client" => ["SubscriptionMode"],
                "ble.notifications" => ["UseInputOperation", "Operation", "WriteMode", "Value"],
                "ble.server" => ["Services"], // Managed by the shared GATT editor.
                "mapper" => ["Engine"],
                _ => []
            };
            foreach (var option in options)
            {
                option.HelperText.ShouldNotBeNullOrWhiteSpace();
                var propertyType = configuration.GetType().GetProperty(option.Name)!.PropertyType;
                if (propertyType == typeof(int) || propertyType == typeof(ushort) || propertyType == typeof(long))
                {
                    option.Editor.ShouldBe(FluxFlow.Authoring.OptionEditorKind.Integer, $"{type}.{option.Name} must not use a floating-point editor.");
                    option.Min.ShouldNotBeNull();
                    if (propertyType != typeof(long))
                    {
                        option.Max.ShouldNotBeNull();
                        option.Max.Value.ShouldBeLessThanOrEqualTo(propertyType == typeof(ushort) ? ushort.MaxValue : int.MaxValue);
                    }
                }
                if (option.DefaultValue is not null)
                {
                    // New GATT nodes deliberately start with an editable example, not an empty server.
                    if (type == "ble.server" && option.Name is "ServiceId" or "Characteristics") continue;
                    var expected = configuration.GetType().GetProperty(option.Name)!.GetValue(configuration);
                    if (expected is Enum) expected = expected.ToString();
                    JsonNode.DeepEquals(JsonSerializer.SerializeToNode(option.DefaultValue),
                        JsonSerializer.SerializeToNode(expected)).ShouldBeTrue(option.Name);
                }
                if (option.IsRequired) option.DefaultValue.ShouldBeNull();
            }
            var ports = description.Palette.Inputs.Concat(description.Palette.SignalInputs).Concat(description.Palette.Outputs)
                .Select(port => port.Name).Append("Error").Append("Type");
            var configurable = configuration.GetType().GetProperties().Select(property => property.Name).Except(ports);
            configurable = configurable.Except(excluded);
            options.Select(option => option.Name).Order().ShouldBe(configurable.Order());
        }
    }

    [Fact]
    public void Registration_is_repeatable_and_shared_between_consumers()
    {
        var services = new ServiceCollection().AddCatalogComponents([]);
        services.ShouldBeEmpty();
        services.AddCatalogComponents([new FluxFlow.Plugins.Bluetooth.BluetoothPlugin().Description]);
        services.AddTransient<DesignerGraphState>();
        using var provider = services.BuildServiceProvider();
        var descriptions = provider.GetServices<IComponentDescription>().ToArray();
        descriptions.Select(description => description.Palette.ComponentType).ShouldBe(
            ["ble.client", "ble.notifications", "ble.server"]);
        var first = provider.GetRequiredService<DesignerGraphState>();
        var second = provider.GetRequiredService<DesignerGraphState>();
        foreach (var description in descriptions)
        {
            first.FindDescription(description.Palette.ComponentType).ShouldBeSameAs(description);
            second.FindDescription(description.Palette.ComponentType).ShouldBeSameAs(description);
            description.Palette.Icon.ShouldNotBeNullOrWhiteSpace();
            description.Palette.Summary.ShouldNotBeNullOrWhiteSpace();
        }
        var firstNode = first.AddNode(descriptions.First());
        firstNode.Configuration["OutputCapacity"] = JsonSerializer.SerializeToElement(9);
        second.AddNode(descriptions.First()).Configuration.ContainsKey("OutputCapacity").ShouldBeFalse();
    }

    [Fact]
    public void Registered_description_drives_new_nodes_and_survives_history_without_configuration_loss()
    {
        var services = new ServiceCollection().AddCatalogComponents(StudioTestServices.InstalledCatalog);
        services.AddSingleton<IComponentDescription, SampleDescription>();
        services.AddTransient<DesignerGraphState>();
        using var provider = services.BuildServiceProvider();
        var graph = provider.GetRequiredService<DesignerGraphState>();
        const string original = """
            {"HostSetting":"keep","Workflows":{"main":{"external":{"Type":"unregistered","Extra":{"A":1}}}}}
            """;
        graph.LoadJson(original);
        graph.ResetHistory();
        var description = graph.FindDescription("sample")!;
        var node = graph.AddNode(description);
        node.NodeName.ShouldBe("sample-1");
        node.Title.ShouldBe("Sample component");
        node.Presentation.Icon.ShouldBe("sample-icon");
        node.DefaultColor.ShouldBe("#123456");
        node.InputPortNames.ShouldBe(["Receive"]);
        node.OutputPortNames.ShouldBe(["Send"]);
        node.Ports.OfType<FlowPortModel>().Select(port => port.Name).ShouldBe(["Receive", "Receive", "Send", "Send"]);
        node.Ports.OfType<FlowPortModel>().Where(port => port.Name == "Send").ShouldAllBe(port => port.ColorRole == "events");
        node.Configuration["Value"].GetInt32().ShouldBe(7);
        var inspector = node.Description.GetInspector(node.Configuration);
        inspector.PrimaryEditor.ShouldBe("Value");
        inspector.Sections.Single().Options.Single().DisplayName.ShouldBe("Sample value");
        inspector.Sections.Single().Options.Single().Editor.ShouldBe(OptionEditorKind.Json);
        node.Configuration["KeepCase"] = JsonSerializer.SerializeToElement(new { InnerName = "same" });
        graph.RecordEdit();
        var saved = graph.ToJson();
        var expected = JsonNode.Parse(original)!;
        expected["Workflows"]!["main"]!["sample-1"] = JsonNode.Parse("""
            {"Type":"sample","Value":7,"KeepCase":{"InnerName":"same"}}
            """);
        JsonNode.DeepEquals(expected, JsonNode.Parse(saved)).ShouldBeTrue();
        graph.Undo();
        JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Redo();
        JsonNode.DeepEquals(expected, JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Description.ShouldBeSameAs(description);
    }

    [Fact]
    public void Import_does_not_apply_new_node_defaults_or_rewrite_existing_values()
    {
        var graph = new DesignerGraphState([new SampleDescription()]);
        const string json = """
            {"Workflows":{"main":{"empty":{"Type":"sample"},"configured":{"Type":"sample","vAlUe":42,"Unknown":[null,true]}}}}
            """;
        graph.LoadJson(json);
        graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(node => node.NodeName == "empty")
            .Configuration.Keys.ShouldBe(["Type"]);
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    [Fact]
    public void Duplicate_type_descriptions_are_rejected_instead_of_picking_one_silently()
    {
        Should.Throw<ArgumentException>(() => new DesignerGraphState([new SampleDescription(), new SampleDescription()]));
    }

    private sealed class SampleDescription : IComponentDescription
    {
        public PaletteItemModel Palette { get; } = new()
        {
            ComponentType = "sample", DisplayName = "Sample component", Category = "Tests",
            Icon = "sample-icon", DefaultColor = "#123456",
            Inputs = [new() { Name = "Receive", Kind = PortKind.Input }],
            Outputs = [new() { Name = "Send", Kind = PortKind.Output, ColorRole = "events" }]
        };
        public NodeInspectorModel Inspector { get; } = new()
        {
            ComponentType = "sample", PrimaryEditor = "Value",
            Sections = [new() { Name = "Configuration", Options = [OptionEditorModel.Create("Value", "Sample value", OptionEditorKind.Json)] }]
        };
        public void InitializeConfiguration(IDictionary<string, JsonElement> configuration) =>
            configuration["Value"] = JsonSerializer.SerializeToElement(7);
    }
}
