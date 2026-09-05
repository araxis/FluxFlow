using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Composition.Tests;

public sealed class MqttFluentCaptureAuthoringTests
{
    [Fact]
    public async Task Fluent_mqtt_captures_preserve_exact_receivers_model_and_registrar_contract()
    {
        var application = new ApplicationDefinitionBuilder();
        application.AddMqttBroker(
                "RootBroker",
                mqtt => mqtt.Host = "root-broker.internal",
                out var rootBroker)
            .ShouldBeSameAs(application);
        application.AddResourceGroup("Messaging", out var messaging);

        var returnedResources = messaging
            .AddMqttBroker(
                "Broker",
                mqtt =>
                {
                    mqtt.Host = "broker.internal";
                    mqtt.Port = 8883;
                    mqtt.UseTls = true;
                },
                out var broker)
            .AddMqttRetryPolicy("Defaults", out var defaults)
            .AddMqttRetryPolicy(
                "Reconnect",
                mqtt =>
                {
                    mqtt.Strategy = MqttRetryStrategy.Fixed;
                    mqtt.InitialDelay = TimeSpan.FromSeconds(3);
                    mqtt.MaximumAttempts = 7;
                },
                out var reconnect)
            .AddMqttSubscription(
                "Commands",
                mqtt =>
                {
                    mqtt.TopicFilter = "commands/#";
                    mqtt.Qos = MqttQos.AtLeastOnce;
                },
                out var commands)
            .AddMqttClient(
                "Client",
                mqtt =>
                {
                    mqtt.ClientId = "fluent-client";
                    mqtt.Broker = broker;
                    mqtt.UseReconnect(reconnect);
                    mqtt.AddSubscription(commands);
                    mqtt.CleanStart = false;
                    mqtt.KeepAlive = TimeSpan.FromSeconds(45);
                },
                out var client);
        application.AddWorkflow("Main", out var workflow);
        var returnedWorkflow = workflow
            .AddMqttCommand(
                "Command",
                mqtt =>
                {
                    mqtt.Client = client;
                    mqtt.MaximumConcurrentRequests = 2;
                },
                out var command)
            .AddMqttPublish(
                "Publish",
                mqtt =>
                {
                    mqtt.Client = client;
                    mqtt.MaximumPendingRequests = 11;
                },
                out var publish)
            .AddMqttReceive(
                "Receive",
                mqtt =>
                {
                    mqtt.Client = client;
                    mqtt.AddSubscription(commands);
                    mqtt.MaximumPendingMessages = 13;
                },
                out var receive)
            .AddMqttEvents(
                "Events",
                mqtt =>
                {
                    mqtt.Client = client;
                    mqtt.MaximumPendingEvents = 17;
                },
                out var events);

        returnedResources.ShouldBeSameAs(messaging);
        returnedWorkflow.ShouldBeSameAs(workflow);
        rootBroker.Address.Value.ShouldBe("Resources.RootBroker");
        broker.Address.Value.ShouldBe("Resources.Messaging.Broker");
        defaults.Address.Value.ShouldBe("Resources.Messaging.Defaults");
        reconnect.Address.Value.ShouldBe("Resources.Messaging.Reconnect");
        commands.Address.Value.ShouldBe("Resources.Messaging.Commands");
        client.Address.Value.ShouldBe("Resources.Messaging.Client");
        command.Input.Address.Value.ShouldBe("Main.Command.Input");
        command.Output.Address.Value.ShouldBe("Main.Command.Output");
        publish.Input.Address.Value.ShouldBe("Main.Publish.Input");
        publish.Output.Address.Value.ShouldBe("Main.Publish.Output");
        receive.Ack.Address.Value.ShouldBe("Main.Receive.Ack");
        receive.Nak.Address.Value.ShouldBe("Main.Receive.Nak");
        receive.Output.Address.Value.ShouldBe("Main.Receive.Output");
        events.Output.Address.Value.ShouldBe("Main.Events.Output");

        var definition = application.Build();
        definition.Resources["RootBroker"].ShouldBeOfType<ResourceInstanceDefinition>()
            .Type.ShouldBe("mqtt.broker");
        var resources = definition.Resources["Messaging"]
            .ShouldBeOfType<ResourceGroupDefinition>()
            .Resources;
        resources.Keys.ShouldBe(
            ["Broker", "Client", "Commands", "Defaults", "Reconnect"],
            ignoreOrder: true);
        resources["Defaults"].ShouldBeOfType<ResourceInstanceDefinition>()
            .Properties.ShouldBeEmpty();
        var clientDefinition = resources["Client"].ShouldBeOfType<ResourceInstanceDefinition>();
        clientDefinition.Type.ShouldBe("mqtt.client");
        clientDefinition.Properties["Broker"].GetString()
            .ShouldBe("Resources.Messaging.Broker");
        clientDefinition.Properties["Reconnect"].GetString()
            .ShouldBe("Resources.Messaging.Reconnect");
        clientDefinition.Properties["Subscriptions"].GetString()
            .ShouldBe("Resources.Messaging.Commands");
        clientDefinition.Properties["CleanStart"].GetBoolean().ShouldBeFalse();
        clientDefinition.Properties["KeepAlive"].GetString().ShouldBe("00:00:45");

        var components = definition.Workflows["Main"].Components;
        components.Keys.ShouldBe(["Command", "Events", "Publish", "Receive"], ignoreOrder: true);
        components["Command"].Type.ShouldBe("mqtt.command");
        components["Command"].Properties["Client"].GetString()
            .ShouldBe("Resources.Messaging.Client");
        components["Command"].Properties["maximumConcurrentRequests"].GetInt32()
            .ShouldBe(2);
        components["Publish"].Type.ShouldBe("mqtt.publish");
        components["Publish"].Properties["maximumPendingRequests"].GetInt32()
            .ShouldBe(11);
        components["Receive"].Type.ShouldBe("mqtt.receive");
        components["Receive"].Properties["subscription"].GetString().ShouldBe("Commands");
        components["Receive"].Properties["maximumPendingMessages"].GetInt32()
            .ShouldBe(13);
        components["Events"].Type.ShouldBe("mqtt.events");
        components["Events"].Properties["maximumPendingEvents"].GetInt32()
            .ShouldBe(17);

        var canonicalJson = ApplicationDefinitionJson.Serialize(definition);
        ApplicationDefinitionJson.Serialize(ApplicationDefinitionJson.Deserialize(canonicalJson))
            .ShouldBe(canonicalJson);

        var hostServices = new ServiceCollection();
        await using var hostProvider = hostServices.BuildServiceProvider();
        var services = new ServiceCollection();
        MqttCompositionResourceRegistrar.Register(services, definition, hostProvider);
        await using var provider = services.BuildServiceProvider();
        var configuration = provider.GetRequiredKeyedService<MqttClientConfiguration>(
            client.Address.Value);

        configuration.Name.ShouldBe("Resources.Messaging.Client");
        configuration.ClientId.ShouldBe("fluent-client");
        configuration.Broker.Host.ShouldBe("broker.internal");
        configuration.Broker.Port.ShouldBe(8883);
        configuration.Broker.UseTls.ShouldBeTrue();
        var retryPolicy = configuration.Reconnect.ShouldNotBeNull().Policy;
        retryPolicy.Strategy.ShouldBe(MqttRetryStrategy.Fixed);
        retryPolicy.InitialDelay.ShouldBe(TimeSpan.FromSeconds(3));
        retryPolicy.MaximumAttempts.ShouldBe(7);
        configuration.Subscriptions.Keys.ShouldBe(["Commands"], ignoreOrder: false);
        configuration.Subscriptions["Commands"].TopicFilter.ShouldBe("commands/#");
        configuration.Subscriptions["Commands"].Qos.ShouldBe(MqttQos.AtLeastOnce);
        configuration.CleanStart.ShouldBeFalse();
        configuration.KeepAlive.ShouldBe(TimeSpan.FromSeconds(45));
    }
}
