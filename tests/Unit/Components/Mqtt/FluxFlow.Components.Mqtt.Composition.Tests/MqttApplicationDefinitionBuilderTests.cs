using FluxFlow.Composition;
using System.Text.Json;
using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Definition;
using FluxFlow.Json.Model;
using FluxFlow.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Composition.Tests;

public sealed class MqttApplicationDefinitionBuilderTests
{
    [Fact]
    public async Task Workflow_resource_contracts_share_registrar_and_preserve_qualified_handles()
    {
        var builder = new ApplicationDefinitionBuilder();
        var broker = builder.AddResource("Connections.broker", MqttResources.Broker, options => options.Host = "shared");
        var workflow = builder.AddWorkflow("orders");
        var client = workflow.AddResource("Connections.client", MqttResources.Client, options =>
        {
            options.ClientId = "orders";
            options.Broker = broker;
        });
        client.Address.Value.ShouldBe("orders.Resources.Connections.client");
        var setup = builder.Build();
        var services = new ServiceCollection();
        await using var host = new ServiceCollection().BuildServiceProvider();
        setup.RegisterResources(new ApplicationResourceRegistrationContext(setup.Definition, 1, "test", host, services));
        await using var provider = services.BuildServiceProvider();
        var configuration = provider.GetRequiredKeyedService<MqttClientConfiguration>(client.Address.Value);
        configuration.Broker.Host.ShouldBe("shared");
        configuration.ClientId.ShouldBe("orders");
        setup.Definition.Workflows["orders"].Resources.ShouldNotBeEmpty();
        Should.Throw<NotSupportedException>(() => setup.ToDefinition());
    }

    [Fact]
    public void Typed_mqtt_definition_projects_resources_components_options_and_ports()
    {
        var builder = new ApplicationDefinitionBuilder();
        var credentials = builder.AddResource("Credentials", "host.credentials");
        var clock = builder.AddResource("Clock", "host.clock");
        var broker = builder.AddMqttBroker("Messaging.Broker", mqtt =>
        {
            mqtt.Host = "broker.internal";
            mqtt.Port = 8883;
            mqtt.UseTls = true;
            mqtt.ServerName = "mqtt.internal";
        });
        var retryCategories = new[] { "Availability", "Transient" };
        var reconnect = builder.AddMqttRetryPolicy("Messaging.Reconnect", mqtt =>
        {
            mqtt.Strategy = MqttRetryStrategy.Exponential;
            mqtt.InitialDelay = TimeSpan.FromSeconds(1);
            mqtt.MaximumDelay = TimeSpan.FromMinutes(1);
            mqtt.MaximumAttempts = 5;
            mqtt.RetryCategories = retryCategories;
        });
        var commands = builder.AddMqttSubscription("Messaging.Commands", mqtt =>
        {
            mqtt.TopicFilter = "commands/+";
            mqtt.Qos = MqttQos.AtLeastOnce;
            mqtt.NoLocal = true;
            mqtt.RetainAsPublished = true;
            mqtt.RetainHandling = MqttRetainHandling.DoNotSend;
        });
        var alerts = builder.AddMqttSubscription("Messaging.Alerts", mqtt =>
        {
            mqtt.TopicFilter = "alerts/#";
            mqtt.Qos = MqttQos.ExactlyOnce;
        });
        var client = builder.AddMqttClient("Messaging.Client", mqtt =>
        {
            mqtt.ClientId = "application-client";
            mqtt.Broker = broker;
            mqtt.UseCredentials(credentials);
            mqtt.UseReconnect(reconnect);
            mqtt.AddSubscription(commands);
            mqtt.AddSubscription(alerts);
            mqtt.CleanStart = false;
            mqtt.KeepAlive = TimeSpan.FromSeconds(45);
            mqtt.AutoConnect = MqttAutoConnectMode.OnStart;
        });
        var workflow = builder.AddWorkflow("Main");
        var command = workflow.AddMqttCommand("Command", mqtt =>
        {
            mqtt.Client = client;
            mqtt.RequestProcessing = MqttRequestProcessing.Concurrent;
            mqtt.ResultOrder = MqttResultOrder.Completion;
            mqtt.MaximumConcurrentRequests = 4;
            mqtt.MaximumPendingRequests = 32;
        });
        var publish = workflow.AddMqttPublish("Publish", mqtt =>
        {
            mqtt.Client = client;
            mqtt.MaximumPendingRequests = 16;
        });
        var receive = workflow.AddMqttReceive("Receive", mqtt =>
        {
            mqtt.Client = client;
            mqtt.AddSubscription(commands);
            mqtt.AddSubscription(new MqttSubscriptionDefinition
            {
                TopicFilter = "inline/#",
                Qos = MqttQos.ExactlyOnce,
                NoLocal = true,
                RetainHandling = MqttRetainHandling.DoNotSend
            });
            mqtt.WorkflowAcknowledgement = MqttWorkflowAcknowledgement.Required;
            mqtt.BrokerAcknowledgement = MqttBrokerAcknowledgement.AfterOutcome;
            mqtt.OutcomeTimeout = TimeSpan.FromSeconds(17);
            mqtt.MaximumPendingMessages = 23;
            mqtt.Clock = clock;
        });
        var events = workflow.AddMqttEvents("Events", mqtt =>
        {
            mqtt.Client = client;
            mqtt.MaximumPendingEvents = 12;
        });
        var handler = workflow.AddComponent("Handler", "sample.handler");
        var outcome = workflow.AddComponent("Outcome", "sample.outcome");
        workflow.Connect(
            receive.Output,
            handler.Input<FlowValue>("Input"));
        workflow.Connect(outcome.Output<string>("Ack"), receive.Ack);
        workflow.Connect(outcome.Output<string>("Nak"), receive.Nak, "failed == true");

        retryCategories[0] = "Changed";
        var definition = builder.Build();

        definition.ResourceContracts.ShouldBe([
            MqttResources.Broker,
            MqttResources.Client,
            MqttResources.Subscription,
            MqttResources.RetryPolicy
        ]);
        definition.ComponentFactories.Keys.OrderBy(x => x).ShouldBe(new[] {
            MqttComponentDefinition.Types.Control, MqttComponentDefinition.Types.Events,
            MqttComponentDefinition.Types.Publish, MqttComponentDefinition.Types.Trigger
        });
        broker.Address.Value.ShouldBe("Resources.Messaging.Broker");
        broker.Name.ShouldBe("Broker");
        broker.ToString().ShouldBe(broker.Address.Value);
        commands.Address.Value.ShouldBe("Resources.Messaging.Commands");
        client.Address.Value.ShouldBe("Resources.Messaging.Client");
        command.Address.Value.ShouldBe("Main.Command");
        command.Name.ShouldBe("Command");
        command.ToString().ShouldBe(command.Address.Value);
        publish.Input.Address.Value.ShouldBe("Main.Publish.Input");
        publish.Output.Address.Value.ShouldBe("Main.Publish.Output");
        receive.Ack.Address.Value.ShouldBe("Main.Receive.Ack");
        receive.Nak.Address.Value.ShouldBe("Main.Receive.Nak");
        InputPortHandle<FlowValue> commandInput = command.Input;
        OutputPortHandle<FlowValue> commandOutput = command.Output;
        OutputPortHandle<FlowValue> receiveOutput = receive.Output;
        OutputPortHandle<FlowValue> eventsOutput = events.Output;
        commandInput.Address.Value.ShouldBe("Main.Command.Input");
        commandOutput.Address.Value.ShouldBe("Main.Command.Output");
        receiveOutput.Address.Value.ShouldBe("Main.Receive.Output");
        eventsOutput.Address.Value.ShouldBe("Main.Events.Output");

        var resourceDefinitions = ResourceInstanceDefinition.Enumerate(definition.Definition.Resources).Where(p => p.Key.StartsWith("Messaging.", StringComparison.Ordinal)).ToDictionary(p => p.Key[10..], p => (ResourceDefinition)p.Value);
        AssertResource(
            resourceDefinitions,
            "Broker",
            "mqtt.broker",
            ("Host", "broker.internal"),
            ("Port", "8883"),
            ("ServerName", "mqtt.internal"),
            ("UseTls", "true"));
        AssertResource(
            resourceDefinitions,
            "Commands",
            "mqtt.subscription",
            ("NoLocal", "true"),
            ("Qos", "AtLeastOnce"),
            ("RetainAsPublished", "true"),
            ("RetainHandling", "DoNotSend"),
            ("TopicFilter", "commands/+"));
        var retry = resourceDefinitions["Reconnect"]
            .ShouldBeOfType<ResourceInstanceDefinition>();
        retry.Type.ShouldBe("retry.policy");
        retry.Properties["Strategy"].GetString().ShouldBe("Exponential");
        retry.Properties["InitialDelay"].GetString().ShouldBe("00:00:01");
        retry.Properties["MaximumDelay"].GetString().ShouldBe("00:01:00");
        retry.Properties["MaximumAttempts"].GetInt32().ShouldBe(5);
        retry.Properties["RetryCategories"].EnumerateArray()
            .Select(static value => value.GetString())
            .ShouldBe(["Availability", "Transient"]);
        var clientDefinition = resourceDefinitions["Client"]
            .ShouldBeOfType<ResourceInstanceDefinition>();
        clientDefinition.Type.ShouldBe("mqtt.client");
        clientDefinition.Properties["ClientId"].GetString().ShouldBe("application-client");
        clientDefinition.Properties["Broker"].GetString()
            .ShouldBe("Resources.Messaging.Broker");
        clientDefinition.Properties["Credentials"].GetString()
            .ShouldBe("Resources.Credentials");
        clientDefinition.Properties["Reconnect"].GetString()
            .ShouldBe("Resources.Messaging.Reconnect");
        clientDefinition.Properties["Subscriptions"].EnumerateArray()
            .Select(static value => value.GetString())
            .ShouldBe([
                "Resources.Messaging.Commands",
                "Resources.Messaging.Alerts"
            ]);
        clientDefinition.Properties["CleanStart"].GetBoolean().ShouldBeFalse();
        clientDefinition.Properties["KeepAlive"].GetString().ShouldBe("00:00:45");
        clientDefinition.Properties["AutoConnect"].GetString().ShouldBe("OnStart");

        var components = definition.Definition.Workflows["Main"].Components;
        AssertComponent(
            components,
            "Command",
            "mqtt.command",
            ("Client", "Resources.Messaging.Client"),
            ("maximumConcurrentRequests", "4"),
            ("maximumPendingRequests", "32"),
            ("requestProcessing", "Concurrent"),
            ("resultOrder", "Completion"));
        AssertComponent(
            components,
            "Publish",
            "mqtt.publish",
            ("Client", "Resources.Messaging.Client"),
            ("maximumPendingRequests", "16"));
        AssertComponent(
            components,
            "Events",
            "mqtt.events",
            ("Client", "Resources.Messaging.Client"),
            ("maximumPendingEvents", "12"));
        var receiveDefinition = components["Receive"];
        receiveDefinition.Type.ShouldBe("mqtt.receive");
        receiveDefinition.Properties["Client"].GetString()
            .ShouldBe("Resources.Messaging.Client");
        receiveDefinition.Properties["Clock"].GetString().ShouldBe("Resources.Clock");
        receiveDefinition.Properties["workflowAcknowledgement"].GetString()
            .ShouldBe("Required");
        receiveDefinition.Properties["brokerAcknowledgement"].GetString()
            .ShouldBe("AfterOutcome");
        receiveDefinition.Properties["outcomeTimeout"].GetString().ShouldBe("00:00:17");
        receiveDefinition.Properties["maximumPendingMessages"].GetInt32().ShouldBe(23);
        var subscriptions = receiveDefinition.Properties["subscription"]
            .EnumerateArray().ToArray();
        subscriptions.Length.ShouldBe(2);
        subscriptions[0].GetString().ShouldBe("Commands");
        subscriptions[1].GetProperty("TopicFilter").GetString().ShouldBe("inline/#");
        subscriptions[1].GetProperty("Qos").GetString().ShouldBe("ExactlyOnce");
        subscriptions[1].GetProperty("NoLocal").GetBoolean().ShouldBeTrue();
        subscriptions[1].GetProperty("RetainHandling").GetString().ShouldBe("DoNotSend");
        receiveDefinition.Properties.ContainsKey("Output").ShouldBeFalse();
        components["Outcome"].Properties.ContainsKey("Ack").ShouldBeFalse();
        components["Outcome"].Properties.ContainsKey("Nak").ShouldBeFalse();

        definition.Links.Count.ShouldBe(3);
        definition.Links[0].Source.ShouldBe(receive.Output.Address);
        definition.Links[0].Target.ShouldBe(handler.Input<MqttReceivedApplicationMessage>("Input").Address);
        definition.Links[0].MessageType.ShouldBe(typeof(FlowValue));
        definition.Links[0].IsConditional.ShouldBeFalse();
        definition.Links[1].Source.ShouldBe(outcome.Output<string>("Ack").Address);
        definition.Links[1].Target.ShouldBe(receive.Ack.Address);
        definition.Links[1].MessageType.ShouldBe(typeof(string));
        definition.Links[1].IsConditional.ShouldBeFalse();
        definition.Links[2].Source.ShouldBe(outcome.Output<string>("Nak").Address);
        definition.Links[2].Target.ShouldBe(receive.Nak.Address);
        definition.Links[2].MessageType.ShouldBe(typeof(string));
        definition.Links[2].ConditionExpression.ShouldBe("failed == true");

        var json = ApplicationDefinitionJson.Serialize(definition.Definition);
        json.ShouldNotContain(nameof(ApplicationSetup.ResourceContracts));
        json.ShouldNotContain(nameof(ApplicationSetup.ComponentFactories));
        var roundTripped = ApplicationDefinitionJson.Deserialize(json);
        roundTripped.GetType().GetProperty(nameof(ApplicationSetup.ResourceContracts)).ShouldBeNull();
        roundTripped.GetType().GetProperty(nameof(ApplicationSetup.ComponentFactories)).ShouldBeNull();
        ApplicationDefinitionJson.Serialize(roundTripped).ShouldBe(json);
    }

    [Theory]
    [InlineData(MqttBrokerTransport.Tcp, false, "/mqtt")]
    [InlineData(MqttBrokerTransport.Tcp, true, "/mqtt")]
    [InlineData(MqttBrokerTransport.WebSocket, false, "/tenant/mqtt")]
    [InlineData(MqttBrokerTransport.WebSocket, true, "/tenant/mqtt")]
    public async Task Typed_broker_transport_matrix_projects_and_resolves_exact_portable_configuration(
        MqttBrokerTransport transport,
        bool useTls,
        string webSocketPath)
    {
        var builder = new ApplicationDefinitionBuilder();
        var broker = builder.AddMqttBroker("Broker", mqtt =>
        {
            mqtt.Host = "broker.internal";
            mqtt.Port = 9443;
            mqtt.Transport = transport;
            mqtt.UseTls = useTls;
            mqtt.WebSocketPath = webSocketPath;
        });
        var client = builder.AddMqttClient("Client", mqtt =>
        {
            mqtt.ClientId = "portable-client";
            mqtt.Broker = broker;
            mqtt.AutoConnect = MqttAutoConnectMode.Disabled;
        });

        var definition = builder.Build();
        var brokerDefinition = definition.Definition.Resources["Broker"]
            .ShouldBeOfType<ResourceInstanceDefinition>();
        brokerDefinition.Type.ShouldBe("mqtt.broker");
        brokerDefinition.Properties.Keys.ShouldBe(
            ["Host", "Port", "Transport", "UseTls", "WebSocketPath"],
            ignoreOrder: true);
        brokerDefinition.Properties["Host"].GetString().ShouldBe("broker.internal");
        brokerDefinition.Properties["Port"].GetInt32().ShouldBe(9443);
        brokerDefinition.Properties["Transport"].GetString().ShouldBe(transport.ToString());
        brokerDefinition.Properties["UseTls"].GetBoolean().ShouldBe(useTls);
        brokerDefinition.Properties["WebSocketPath"].GetString().ShouldBe(webSocketPath);

        await using var hostProvider = new ServiceCollection().BuildServiceProvider();
        var services = new ServiceCollection();
        MqttCompositionResourceRegistrar.Register(services, definition.Definition, hostProvider);
        await using var provider = services.BuildServiceProvider();
        var configuration = provider.GetRequiredKeyedService<MqttClientConfiguration>(
            client.Address.Value);

        configuration.Broker.Host.ShouldBe("broker.internal");
        configuration.Broker.Port.ShouldBe(9443);
        configuration.Broker.Transport.ShouldBe(transport);
        configuration.Broker.UseTls.ShouldBe(useTls);
        configuration.Broker.WebSocketPath.ShouldBe(webSocketPath);
    }

    [Fact]
    public async Task Typed_mqtt_definition_remains_compatible_with_resource_registration()
    {
        var builder = new ApplicationDefinitionBuilder();
        var credentials = builder.AddResource("Credentials", "host.credentials");
        var broker = builder.AddMqttBroker("Messaging.Broker", mqtt =>
        {
            mqtt.Host = "broker.internal";
            mqtt.Port = 8883;
            mqtt.UseTls = true;
        });
        var subscription = builder.AddMqttSubscription("Messaging.Commands", mqtt =>
        {
            mqtt.TopicFilter = "commands/#";
            mqtt.Qos = MqttQos.AtLeastOnce;
        });
        var client = builder.AddMqttClient("Messaging.Client", mqtt =>
        {
            mqtt.ClientId = "typed-client";
            mqtt.Broker = broker;
            mqtt.UseCredentials(credentials);
            mqtt.AddSubscription(subscription);
        });
        var definition = builder.Build();
        var hostServices = new ServiceCollection();
        hostServices.AddKeyedSingleton(
            credentials.Address.Value,
            new MqttCredentialConfiguration { Username = "host-user" });
        await using var hostProvider = hostServices.BuildServiceProvider();
        var services = new ServiceCollection();

        MqttCompositionResourceRegistrar.Register(services, definition.Definition, hostProvider);
        await using var provider = services.BuildServiceProvider();
        var configuration = provider.GetRequiredKeyedService<MqttClientConfiguration>(
            client.Address.Value);

        configuration.Name.ShouldBe("Resources.Messaging.Client");
        configuration.ClientId.ShouldBe("typed-client");
        configuration.Broker.Host.ShouldBe("broker.internal");
        configuration.Broker.Port.ShouldBe(8883);
        configuration.Broker.Transport.ShouldBe(MqttBrokerTransport.Tcp);
        configuration.Broker.UseTls.ShouldBeTrue();
        configuration.Broker.WebSocketPath.ShouldBe("/mqtt");
        configuration.Credentials.ShouldNotBeNull().Username.ShouldBe("host-user");
        configuration.Subscriptions.Keys.ShouldBe(["Commands"], ignoreOrder: false);
        configuration.Subscriptions["Commands"].TopicFilter.ShouldBe("commands/#");
        configuration.Subscriptions["Commands"].Qos.ShouldBe(MqttQos.AtLeastOnce);
    }

    [Fact]
    public async Task Typed_mqtt_client_projects_certificates_and_last_will_to_registrar_schema()
    {
        var builder = new ApplicationDefinitionBuilder();
        var broker = builder.AddMqttBroker(
            "Broker",
            mqtt => mqtt.Host = "localhost");
        var client = builder.AddMqttClient("Client", mqtt =>
        {
            mqtt.ClientId = "secured-client";
            mqtt.Broker = broker;
            mqtt.Certificates =
            [
                new MqttClientCertificate
                {
                    Name = "client.pfx",
                    Content = new byte[] { 0, 1, 2 },
                    Password = "certificate-password"
                }
            ];
            mqtt.LastWill = new MqttPublishMessage
            {
                Topic = "clients/secured-client/status",
                Content = FlowContent.FromBytes(
                    new byte[] { 3, 4 },
                    "application/octet-stream",
                    "binary"),
                Qos = MqttQos.AtLeastOnce,
                Retain = true,
                ResponseTopic = "clients/responses",
                CorrelationData = "correlation-1",
                UserProperties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["tenant"] = "north"
                }
            };
        });
        var definition = builder.Build();

        var properties = definition.Definition.Resources["Client"]
            .ShouldBeOfType<ResourceInstanceDefinition>()
            .Properties;
        var certificate = properties["Certificates"].EnumerateArray().ShouldHaveSingleItem();
        certificate.EnumerateObject().Select(static property => property.Name)
            .ShouldBe(["ContentBase64", "Name", "Password"], ignoreOrder: true);
        certificate.GetProperty("Name").GetString().ShouldBe("client.pfx");
        certificate.GetProperty("ContentBase64").GetString().ShouldBe("AAEC");
        certificate.GetProperty("Password").GetString().ShouldBe("certificate-password");
        var lastWill = properties["LastWill"];
        lastWill.TryGetProperty("Content", out _).ShouldBeFalse();
        lastWill.TryGetProperty("bytes", out _).ShouldBeFalse();
        lastWill.GetProperty("Topic").GetString()
            .ShouldBe("clients/secured-client/status");
        lastWill.GetProperty("ContentBase64").GetString().ShouldBe("AwQ=");
        lastWill.GetProperty("ContentType").GetString()
            .ShouldBe("application/octet-stream");
        lastWill.GetProperty("Encoding").GetString().ShouldBe("binary");
        lastWill.GetProperty("Qos").GetString().ShouldBe("AtLeastOnce");
        lastWill.GetProperty("Retain").GetBoolean().ShouldBeTrue();
        lastWill.GetProperty("ResponseTopic").GetString().ShouldBe("clients/responses");
        lastWill.GetProperty("CorrelationData").GetString().ShouldBe("correlation-1");
        lastWill.GetProperty("UserProperties").GetProperty("tenant")
            .GetString().ShouldBe("north");

        var hostServices = new ServiceCollection()
            .AddSingleton<IMqttInlineSecretPolicy, AllowInlineSecrets>();
        await using var hostProvider = hostServices.BuildServiceProvider();
        var services = new ServiceCollection();
        MqttCompositionResourceRegistrar.Register(services, definition.Definition, hostProvider);
        await using var provider = services.BuildServiceProvider();

        var configuration = provider.GetRequiredKeyedService<MqttClientConfiguration>(
            client.Address.Value);
        var resolvedCertificate = configuration.Certificates.ShouldHaveSingleItem();
        resolvedCertificate.Name.ShouldBe("client.pfx");
        resolvedCertificate.Content.ToArray().ShouldBe([0, 1, 2]);
        resolvedCertificate.Password.ShouldBe("certificate-password");
        var resolvedLastWill = configuration.LastWill.ShouldNotBeNull();
        resolvedLastWill.Topic.ShouldBe("clients/secured-client/status");
        resolvedLastWill.Content.Bytes.ToArray().ShouldBe([3, 4]);
        resolvedLastWill.Content.ContentType.ShouldBe("application/octet-stream");
        resolvedLastWill.Content.Encoding.ShouldBe("binary");
        resolvedLastWill.Qos.ShouldBe(MqttQos.AtLeastOnce);
        resolvedLastWill.Retain.ShouldBeTrue();
        resolvedLastWill.ResponseTopic.ShouldBe("clients/responses");
        resolvedLastWill.CorrelationData.ShouldBe("correlation-1");
        resolvedLastWill.UserProperties["tenant"].ShouldBe("north");
    }

    [Fact]
    public void Typed_mqtt_client_definition_projects_disabled_inline_and_resource_reconnect_choices()
    {
        var builder = new ApplicationDefinitionBuilder();
        var broker = builder.AddMqttBroker(
            "Messaging.Broker",
            mqtt => mqtt.Host = "localhost");
        var retry = builder.AddMqttRetryPolicy("Messaging.Retry", mqtt =>
        {
            mqtt.Strategy = MqttRetryStrategy.Fixed;
            mqtt.InitialDelay = TimeSpan.FromSeconds(3);
        });
        builder.AddMqttClient("Messaging.Disabled", mqtt =>
        {
            mqtt.ClientId = "disabled";
            mqtt.Broker = broker;
            mqtt.DisableReconnect();
        });
        builder.AddMqttClient("Messaging.Inline", mqtt =>
        {
            mqtt.ClientId = "inline";
            mqtt.Broker = broker;
            mqtt.UseReconnect(new MqttRetryPolicy
            {
                Strategy = MqttRetryStrategy.Linear,
                InitialDelay = TimeSpan.FromSeconds(2),
                MaximumAttempts = 3
            });
        });
        builder.AddMqttClient("Messaging.Referenced", mqtt =>
        {
            mqtt.ClientId = "referenced";
            mqtt.Broker = broker;
            mqtt.UseReconnect(retry);
        });

        var resources = ResourceInstanceDefinition.Enumerate(builder.Build().Definition.Resources).Where(p => p.Key.StartsWith("Messaging.", StringComparison.Ordinal)).ToDictionary(p => p.Key[10..], p => (ResourceDefinition)p.Value);

        resources["Disabled"].ShouldBeOfType<ResourceInstanceDefinition>()
            .Properties["Reconnect"].ValueKind.ShouldBe(JsonValueKind.False);
        var inline = resources["Inline"].ShouldBeOfType<ResourceInstanceDefinition>()
            .Properties["Reconnect"];
        inline.ValueKind.ShouldBe(JsonValueKind.Object);
        inline.GetProperty("Strategy").GetString().ShouldBe("Linear");
        inline.GetProperty("InitialDelay").GetString().ShouldBe("00:00:02");
        inline.GetProperty("MaximumAttempts").GetInt32().ShouldBe(3);
        resources["Referenced"].ShouldBeOfType<ResourceInstanceDefinition>()
            .Properties["Reconnect"].GetString()
            .ShouldBe("Resources.Messaging.Retry");
    }

    [Fact]
    public void Typed_mqtt_validation_failures_are_atomic_and_allow_same_name_retry()
    {
        var builder = new ApplicationDefinitionBuilder();
        var credentials = builder.AddResource("Credentials", "host.credentials");

        Should.Throw<InvalidOperationException>(() =>
            builder.AddMqttBroker("Messaging.Broker", _ => { }));
        Should.Throw<InvalidOperationException>(() => builder.AddMqttBroker(
            "Messaging.WhitespaceBroker",
            mqtt => mqtt.Host = " "));
        var broker = builder.AddMqttBroker(
            "Messaging.Broker",
            mqtt => mqtt.Host = "localhost");

        Should.Throw<InvalidOperationException>(() =>
            builder.AddMqttSubscription("Messaging.Commands", _ => { }));
        Should.Throw<InvalidOperationException>(() => builder.AddMqttSubscription(
            "Messaging.WhitespaceSubscription",
            mqtt => mqtt.TopicFilter = " "));
        var commands = builder.AddMqttSubscription(
            "Messaging.Commands",
            mqtt => mqtt.TopicFilter = "commands/#");

        Should.Throw<InvalidOperationException>(() =>
            builder.AddMqttClient("Messaging.Client", mqtt => mqtt.ClientId = "client"));
        Should.Throw<InvalidOperationException>(() => builder.AddMqttClient(
            "Messaging.WhitespaceClient",
            mqtt =>
            {
                mqtt.ClientId = " ";
                mqtt.Broker = broker;
            }));
        var client = builder.AddMqttClient("Messaging.Client", mqtt =>
        {
            mqtt.ClientId = "client";
            mqtt.Broker = broker;
            mqtt.AddSubscription(commands);
        });
        Should.Throw<InvalidOperationException>(() => builder.AddMqttClient(
            "Messaging.ConflictingCredentials",
            mqtt =>
            {
                mqtt.ClientId = "conflict";
                mqtt.Broker = broker;
                mqtt.Credentials = new MqttCredentialConfiguration { Username = "inline" };
                mqtt.UseCredentials(credentials);
            }));
        builder.AddMqttClient("Messaging.ConflictingCredentials", mqtt =>
        {
            mqtt.ClientId = "resolved";
            mqtt.Broker = broker;
        });

        var workflow = builder.AddWorkflow("Main");
        Should.Throw<InvalidOperationException>(() =>
            workflow.AddMqttCommand("Command", _ => { }));
        workflow.AddMqttCommand("Command", mqtt => mqtt.Client = client);
        Should.Throw<InvalidOperationException>(() =>
            workflow.AddMqttReceive("Receive", mqtt => mqtt.Client = client));
        workflow.AddMqttReceive("Receive", mqtt =>
        {
            mqtt.Client = client;
            mqtt.AddSubscription(commands);
        });

        var definition = builder.Build();

        var resources = ResourceInstanceDefinition.Enumerate(definition.Definition.Resources).Where(p => p.Key.StartsWith("Messaging.", StringComparison.Ordinal)).ToDictionary(p => p.Key[10..], p => (ResourceDefinition)p.Value);
        resources.Keys.ShouldBe(
            ["Broker", "Client", "Commands", "ConflictingCredentials"],
            ignoreOrder: true);
        resources["Broker"].ShouldBeOfType<ResourceInstanceDefinition>()
            .Properties.Keys.ShouldBe(["Host"], ignoreOrder: false);
        resources["Commands"].ShouldBeOfType<ResourceInstanceDefinition>()
            .Properties.Keys.ShouldBe(["TopicFilter"], ignoreOrder: false);
        var clientProperties = resources["Client"]
            .ShouldBeOfType<ResourceInstanceDefinition>()
            .Properties;
        clientProperties.Keys.ShouldBe(
            ["Broker", "ClientId", "Subscriptions"],
            ignoreOrder: true);
        clientProperties["Subscriptions"].GetString()
            .ShouldBe("Resources.Messaging.Commands");
        var components = definition.Definition.Workflows["Main"].Components;
        components.Keys
            .ShouldBe(["Command", "Receive"], ignoreOrder: true);
        components["Command"].Properties.Keys.ShouldBe(["Client"], ignoreOrder: false);
        components["Receive"].Properties.Keys.ShouldBe(
            ["Client", "subscription"],
            ignoreOrder: true);
        components["Receive"].Properties["subscription"].GetString()
            .ShouldBe("Commands");
    }

    private static void AssertResource(
        IReadOnlyDictionary<string, ResourceDefinition> resources,
        string name,
        string type,
        params (string Name, string RawValue)[] expectedProperties)
    {
        var resource = resources[name].ShouldBeOfType<ResourceInstanceDefinition>();
        resource.Type.ShouldBe(type);
        resource.Properties.Keys.ShouldBe(
            expectedProperties.Select(static property => property.Name),
            ignoreOrder: true);
        foreach (var (propertyName, expected) in expectedProperties)
            ReadScalar(resource.Properties[propertyName]).ShouldBe(expected);
    }

    private static void AssertComponent(
        IReadOnlyDictionary<string, ComponentDefinition> components,
        string name,
        string type,
        params (string Name, string RawValue)[] expectedProperties)
    {
        var component = components[name];
        component.Type.ShouldBe(type);
        component.Properties.Keys.ShouldBe(
            expectedProperties.Select(static property => property.Name),
            ignoreOrder: true);
        foreach (var (propertyName, expected) in expectedProperties)
            ReadScalar(component.Properties[propertyName]).ShouldBe(expected);
    }

    private static string ReadScalar(JsonElement value)
        => value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : value.GetRawText();

    private sealed class AllowInlineSecrets : IMqttInlineSecretPolicy
    {
        public bool IsAllowed(ApplicationAddress client, string propertyName) => true;
    }
}


