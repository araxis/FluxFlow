using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Model;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace FluxFlow.Components.Mqtt.Composition.Tests;

public sealed class MqttServiceCollectionExtensionsTests
{
    private const string ClientAddress = "Resources.Messaging.Client1";

    [Fact]
    public void AddMqtt_registers_four_vnext_contracts()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddFluxFlowComponents().AddMqtt());

        registry.Components.Keys.ShouldBe([
            MqttComponentDefinition.Types.Control,
            MqttComponentDefinition.Types.Events,
            MqttComponentDefinition.Types.Publish,
            MqttComponentDefinition.Types.Trigger
        ], ignoreOrder: false);
        AssertMessagePort<MqttClientRequest>(
            registry.Components[MqttComponentDefinition.Types.Control].Inputs,
            MqttComponentDefinition.Ports.Input);
        AssertMessagePort<MqttClientResult>(
            registry.Components[MqttComponentDefinition.Types.Control].Outputs,
            MqttComponentDefinition.Ports.Output);
        AssertMessagePort<MqttPublishMessage>(
            registry.Components[MqttComponentDefinition.Types.Publish].Inputs,
            MqttComponentDefinition.Ports.Input);
        AssertMessagePort<MqttClientResult>(
            registry.Components[MqttComponentDefinition.Types.Publish].Outputs,
            MqttComponentDefinition.Ports.Output);

        var trigger = registry.Components[MqttComponentDefinition.Types.Trigger];
        AssertSignalPort(trigger.Inputs, MqttComponentDefinition.Ports.Ack);
        AssertSignalPort(trigger.Inputs, MqttComponentDefinition.Ports.Nak);
        AssertMessagePort<MqttReceivedApplicationMessage>(
            trigger.Outputs,
            MqttComponentDefinition.Ports.Output);

        var events = registry.Components[MqttComponentDefinition.Types.Events];
        events.Inputs.ShouldBeEmpty();
        AssertMessagePort<MqttClientEvent>(events.Outputs, MqttComponentDefinition.Ports.Output);

        registry.TryGetDescriptor("mqtt.control", out _).ShouldBeFalse();
        registry.TryGetDescriptor("mqtt.trigger", out _).ShouldBeFalse();
    }

    [Fact]
    public void Design_metadata_describes_four_nodes_shared_client_and_signal_inputs()
    {
        var metadata = DesignMetadataByType();

        metadata.Keys.ShouldBe([
            MqttComponentDefinition.Types.Control,
            MqttComponentDefinition.Types.Publish,
            MqttComponentDefinition.Types.Trigger,
            MqttComponentDefinition.Types.Events
        ], ignoreOrder: false);

        foreach (var item in metadata.Values)
        {
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Category.ShouldBe(new ComponentCategory("MQTT"));

            var client = item.Resources.Single(resource =>
                resource.Name.Value == MqttComponentDefinition.Resources.Client);
            client.IsRequired.ShouldBeTrue();
            client.ValueType?.Value.ShouldBe(nameof(IMqttClientController));
            AssertResourceHints(
                client,
                ResourceDesignMetadataAttributeValues.Client,
                "Resources.{name}");
        }

        var command = metadata[MqttComponentDefinition.Types.Control];
        command.DisplayName?.Value.ShouldBe("MQTT Command");
        command.PreferredNodeName.ShouldBe(new ComponentPreferredNodeName("mqttCommand"));

        var trigger = metadata[MqttComponentDefinition.Types.Trigger];
        trigger.DisplayName?.Value.ShouldBe("MQTT Receive");
        trigger.PreferredNodeName.ShouldBe(new ComponentPreferredNodeName("mqttReceive"));
        trigger.Ports.Single(port => port.Name.Value == MqttComponentDefinition.Ports.Ack)
            .Attributes[new ComponentAttributeName(PortDesignMetadataAttributeNames.Kind)]
            .Value.ShouldBe(PortDesignMetadataAttributeValues.Signal);
        trigger.Ports.Single(port => port.Name.Value == MqttComponentDefinition.Ports.Nak)
            .Attributes[new ComponentAttributeName(PortDesignMetadataAttributeNames.Kind)]
            .Value.ShouldBe(PortDesignMetadataAttributeValues.Signal);

        var clock = trigger.Resources.Single(resource =>
            resource.Name.Value == MqttComponentDefinition.Resources.Clock);
        clock.IsRequired.ShouldBeFalse();
        AssertResourceHints(
            clock,
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");

        AssertOptionHints(
            metadata[MqttComponentDefinition.Types.Control],
            "maximumConcurrentRequests",
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            metadata[MqttComponentDefinition.Types.Trigger],
            "subscription",
            "Subscription",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(
            metadata[MqttComponentDefinition.Types.Events],
            "maximumPendingEvents",
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public async Task Canonical_resources_bind_nested_addresses_shared_broker_and_scalar_or_array_subscriptions()
    {
        var definition = Parse(CanonicalDefinitionJson);
        var hostServices = new ServiceCollection()
            .AddSingleton<IMqttTransportFactory, UnusedTransportFactory>();
        hostServices.AddKeyedSingleton(
            "Resources.Messaging.Credentials",
            new MqttCredentialConfiguration
            {
                Username = "referenced-user",
                Password = "host-secret"
            });
        await using var hostProvider = hostServices.BuildServiceProvider();
        var services = new ServiceCollection();
        RegisterResources(services, definition, hostProvider);

        await using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredKeyedService<MqttClientConfiguration>(ClientAddress);
        var second = provider.GetRequiredKeyedService<MqttClientConfiguration>(
            "Resources.Messaging.Client2");

        first.Name.ShouldBe(ClientAddress);
        first.ClientId.ShouldBe("client-1");
        first.Broker.Host.ShouldBe("broker.internal");
        first.Broker.ShouldBeSameAs(second.Broker);
        first.Credentials!.Username.ShouldBe("direct-user");
        first.Credentials.Password.ShouldBe("host-secret");
        first.Subscriptions.Keys.ShouldBe(["Commands"], ignoreOrder: false);
        second.Subscriptions.Keys.ShouldBe(["Commands", "Alerts"], ignoreOrder: true);
        first.Reconnect.Policy.Strategy.ShouldBe(MqttRetryStrategy.Linear);
        first.LastWill!.Content.Bytes.ToArray().ShouldBe([0, 1, 2, 3]);
        provider.GetKeyedService<MqttCredentialConfiguration>(
            "Resources.Messaging.Credentials").ShouldBeNull();
        hostProvider.GetKeyedService<MqttBrokerConfiguration>(
            "Resources.Messaging.Broker").ShouldBeNull();

        var firstController = provider.GetRequiredKeyedService<IMqttClientController>(ClientAddress);
        provider.GetRequiredKeyedService<IMqttClientController>(ClientAddress)
            .ShouldBeSameAs(firstController);
        provider.GetRequiredKeyedService<IMqttClientController>("Resources.Messaging.Client2")
            .ShouldNotBeSameAs(firstController);
    }

    [Fact]
    public async Task Revision_provider_owns_container_created_controller_not_host_provider()
    {
        var definition = Parse(CanonicalDefinitionJson);
        var controller = new RecordingController();
        var hostServices = new ServiceCollection()
            .AddSingleton<IMqttTransportFactory, UnusedTransportFactory>();
        hostServices.AddKeyedSingleton(
            "Resources.Messaging.Credentials",
            new MqttCredentialConfiguration { Password = "host-secret" });
        await using var hostProvider = hostServices.BuildServiceProvider();
        var revisionServices = new ServiceCollection();
        revisionServices.AddKeyedSingleton<IMqttClientController>(
            ClientAddress,
            (_, _) => controller);
        RegisterResources(revisionServices, definition, hostProvider);

        var revisionProvider = revisionServices.BuildServiceProvider();
        revisionProvider.GetRequiredKeyedService<IMqttClientController>(ClientAddress)
            .ShouldBeSameAs(controller);
        var configuration = revisionProvider
            .GetRequiredKeyedService<MqttClientConfiguration>(ClientAddress);
        configuration.Broker.Host.ShouldBe("broker.internal");
        configuration.Credentials.ShouldNotBeNull().Password.ShouldBe("host-secret");

        await revisionProvider.DisposeAsync();
        controller.DisposeCalls.ShouldBe(1);
        await hostProvider.DisposeAsync();
        controller.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Keyed_host_transport_and_clock_bridge_into_revision_controller()
    {
        const string clientAddress = "Resources.Client";
        var definition = Parse("""
            {
              "Resources": {
                "Broker": { "Type": "mqtt.broker", "Host": "localhost" },
                "Client": {
                  "Type": "mqtt.client",
                  "ClientId": "keyed-client",
                  "Broker": "Resources.Broker",
                  "AutoConnect": "Disabled"
                }
              },
              "Workflows": {}
            }
            """);
        var transportFactory = new RecordingTransportFactory();
        var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-07-28T10:00:00Z"));
        var hostServices = new ServiceCollection();
        hostServices.AddKeyedSingleton<IMqttTransportFactory>(clientAddress, transportFactory);
        hostServices.AddKeyedSingleton<TimeProvider>(clientAddress, clock);
        await using var hostProvider = hostServices.BuildServiceProvider();
        var revisionServices = new ServiceCollection();
        RegisterResources(revisionServices, definition, hostProvider);
        await using var revisionProvider = revisionServices.BuildServiceProvider();

        hostProvider.GetService<IMqttTransportFactory>().ShouldBeNull();
        var controller = revisionProvider
            .GetRequiredKeyedService<IMqttClientController>(clientAddress)
            .ShouldBeOfType<MqttClientController>();
        await controller.StartAsync();
        var status = (await controller.ExecuteAsync(new MqttStatusRequest()))
            .ShouldBeOfType<MqttStatusResult>();
        transportFactory.CreateCalls.ShouldBe(1);
        status.Timestamp.ShouldBe(clock.GetUtcNow());
        status.Status.Timestamp.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public async Task Host_credentials_certificates_and_inline_policy_bridge_into_revision_configuration()
    {
        const string credentialAddress = "Resources.Credentials";
        const string certificateAddress = "Resources.Certificate";
        var definition = Parse("""
            {
              "Resources": {
                "Broker": { "Type": "mqtt.broker", "Host": "localhost" },
                "Credentials": { "Type": "host.credentials" },
                "Certificate": { "Type": "host.certificate" },
                "Client": {
                  "Type": "mqtt.client",
                  "ClientId": "secured-client",
                  "Broker": "Resources.Broker",
                  "Credentials": "Resources.Credentials",
                  "Password": "inline-override",
                  "Certificates": [
                    "Resources.Certificate",
                    {
                      "Name": "inline-certificate",
                      "ContentBase64": "AQID",
                      "Password": "inline-certificate-password"
                    }
                  ]
                }
              },
              "Workflows": {}
            }
            """);
        var credentials = new MqttCredentialConfiguration
        {
            Username = "host-user",
            Password = "host-password"
        };
        var certificate = new MqttClientCertificate
        {
            Name = "host-certificate",
            Content = new byte[] { 9, 8, 7 },
            Password = "host-certificate-password"
        };
        var policy = new RecordingInlineSecretPolicy();
        var hostServices = new ServiceCollection()
            .AddSingleton<IMqttTransportFactory, UnusedTransportFactory>()
            .AddSingleton<IMqttInlineSecretPolicy>(policy);
        hostServices.AddKeyedSingleton(credentialAddress, credentials);
        hostServices.AddKeyedSingleton(certificateAddress, certificate);
        await using var hostProvider = hostServices.BuildServiceProvider();
        var revisionServices = new ServiceCollection();
        RegisterResources(revisionServices, definition, hostProvider);
        await using var revisionProvider = revisionServices.BuildServiceProvider();

        var configuration = revisionProvider
            .GetRequiredKeyedService<MqttClientConfiguration>("Resources.Client");

        configuration.Credentials.ShouldNotBeNull().Username.ShouldBe("host-user");
        configuration.Credentials.Password.ShouldBe("inline-override");
        configuration.Certificates.Count.ShouldBe(2);
        configuration.Certificates[0].ShouldBeSameAs(certificate);
        configuration.Certificates[1].Name.ShouldBe("inline-certificate");
        configuration.Certificates[1].Content.ToArray().ShouldBe([1, 2, 3]);
        policy.Requests.ShouldBe([
            ("Resources.Client", "Credentials.Password"),
            ("Resources.Client", "Certificates")
        ], ignoreOrder: false);
        revisionProvider.GetKeyedService<MqttCredentialConfiguration>(credentialAddress)
            .ShouldBeNull();
        revisionProvider.GetKeyedService<MqttClientCertificate>(certificateAddress)
            .ShouldBeNull();
    }

    [Fact]
    public async Task Code_first_definition_embeds_mqtt_components_and_resources_without_AddMqtt()
    {
        var builder = new ApplicationDefinitionBuilder();
        var broker = builder.AddMqttBroker(
            "Broker",
            mqtt => mqtt.Host = "code-first-broker.internal");
        var client = builder.AddMqttClient("Client", mqtt =>
        {
            mqtt.ClientId = "code-first-client";
            mqtt.Broker = broker;
            mqtt.AutoConnect = MqttAutoConnectMode.OnStart;
            mqtt.DisableReconnect();
        });
        builder.AddWorkflow("Main").AddMqttCommand("Command", mqtt =>
        {
            mqtt.Client = client;
            mqtt.MaximumConcurrentRequests = 2;
        });
        var definition = builder.Build();
        var transport = new RecordingTransportFactory();

        await using var host = await CanonicalApplicationTestHost.StartAsync(
            definition,
            static _ => { },
            services => services.AddSingleton<IMqttTransportFactory>(transport));

        host.StartResult.Succeeded.ShouldBeTrue();
        host.Application.CurrentDefinition.ShouldBeSameAs(definition);
        definition.ComponentDescriptors.ShouldHaveSingleItem()
            .Type.ShouldBe(MqttComponentDefinition.Types.Control);
        definition.ApplicationResourceContracts.Select(static contract => contract.Type)
            .ShouldBe([
                MqttComponentDefinition.ResourceTypes.Broker,
                MqttComponentDefinition.ResourceTypes.Client
            ], ignoreOrder: true);
        var configuration = transport.Configurations.ShouldHaveSingleItem();
        configuration.Name.ShouldBe(client.Address.Value);
        configuration.ClientId.ShouldBe("code-first-client");
        configuration.Broker.Host.ShouldBe("code-first-broker.internal");
        var session = transport.Sessions.ShouldHaveSingleItem();
        session.ConnectCalls.ShouldBe(1);
        session.DisposeCalls.ShouldBe(0);

        await host.Application.StopAsync();

        session.DisconnectCalls.ShouldBe(1);
        session.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Engine_failure_replacement_and_stop_dispose_each_revision_controller_once()
    {
        var controllers = new List<RecordingController>();
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            CreateEngineDefinition(maximumConcurrentRequests: 4),
            services => services.AddFluxFlowComponents().AddMqtt(),
            configureHostServices: services =>
            {
                services.AddSingleton<IMqttTransportFactory, UnusedTransportFactory>();
                services.AddKeyedSingleton(
                    "Resources.Credentials",
                    new MqttCredentialConfiguration { Password = "host-secret" });
            },
            registerResources: context =>
            {
                var controller = new RecordingController();
                controllers.Add(controller);
                context.Services.AddKeyedSingleton<IMqttClientController>(
                    "Resources.Client",
                    (_, _) => controller);
            });

        host.StartResult.Succeeded.ShouldBeTrue();
        controllers.ShouldHaveSingleItem().StartCalls.ShouldBe(1);

        var rejected = await host.Application.ApplyAsync(
            "invalid-options",
            CreateEngineDefinition(maximumConcurrentRequests: 0));

        rejected.IsRejected.ShouldBeTrue();
        controllers.Count.ShouldBe(2);
        controllers[0].DisposeCalls.ShouldBe(0);
        controllers[1].StartCalls.ShouldBe(1);
        controllers[1].DisposeCalls.ShouldBe(1);

        var replaced = await host.Application.ApplyAsync(
            "replacement",
            CreateEngineDefinition(maximumConcurrentRequests: 8));

        replaced.IsApplied.ShouldBeTrue();
        controllers.Count.ShouldBe(3);
        controllers[0].DisposeCalls.ShouldBe(1);
        controllers[2].StartCalls.ShouldBe(1);
        controllers[2].DisposeCalls.ShouldBe(0);

        await host.Application.StopAsync();
        controllers[2].DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Canonical_revisions_use_real_controllers_and_own_each_transport_session_once()
    {
        const string initialSecret = "initial-revision-secret";
        const string candidateSecret = "candidate-revision-secret";
        const string replacementSecret = "replacement-revision-secret";
        var defaultFactory = new RecordingTransportFactory();
        var replacementFactory = new RecordingTransportFactory();
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            CreateControllerRevisionDefinition(
                clientResource: "Client",
                brokerHost: "initial-broker.internal",
                clientId: "initial-client",
                subscriptionResource: "InitialSubscription",
                topicFilter: "initial/+",
                password: initialSecret,
                maximumConcurrentRequests: 4),
            services => services.AddFluxFlowComponents().AddMqtt(),
            configureHostServices: services =>
            {
                services.AddSingleton<IMqttTransportFactory>(defaultFactory);
                services.AddKeyedSingleton<IMqttTransportFactory>(
                    "Resources.ReplacementClient",
                    replacementFactory);
                services.AddSingleton<IMqttInlineSecretPolicy>(new RecordingInlineSecretPolicy());
            });

        host.StartResult.Succeeded.ShouldBeTrue();
        var initialConfiguration = defaultFactory.Configurations.ShouldHaveSingleItem();
        initialConfiguration.Name.ShouldBe("Resources.Client");
        initialConfiguration.Broker.Host.ShouldBe("initial-broker.internal");
        initialConfiguration.ClientId.ShouldBe("initial-client");
        initialConfiguration.Credentials.ShouldNotBeNull().Password.ShouldBe(initialSecret);
        initialConfiguration.Subscriptions.Keys.ShouldBe(["InitialSubscription"], ignoreOrder: false);
        initialConfiguration.Subscriptions["InitialSubscription"].TopicFilter.ShouldBe("initial/+");
        var initialSession = defaultFactory.Sessions.ShouldHaveSingleItem();
        initialSession.ConnectCalls.ShouldBe(1);
        initialSession.Subscribed.ShouldHaveSingleItem().Subscription.TopicFilter.ShouldBe("initial/+");
        initialSession.DisposeCalls.ShouldBe(0);

        var rejected = await host.Application.ApplyAsync(
            "rejected-candidate",
            CreateControllerRevisionDefinition(
                clientResource: "Client",
                brokerHost: "candidate-broker.internal",
                clientId: "candidate-client",
                subscriptionResource: "CandidateSubscription",
                topicFilter: "candidate/#",
                password: candidateSecret,
                maximumConcurrentRequests: 0));

        rejected.IsRejected.ShouldBeTrue();
        rejected.Diagnostics.ShouldNotBeEmpty();
        var diagnosticText = JsonSerializer.Serialize(rejected.Diagnostics);
        diagnosticText.ShouldNotContain(initialSecret);
        diagnosticText.ShouldNotContain(candidateSecret);
        diagnosticText.ShouldNotContain(replacementSecret);
        defaultFactory.Configurations.Count.ShouldBe(2);
        defaultFactory.Sessions.Count.ShouldBe(2);
        var candidateConfiguration = defaultFactory.Configurations[1];
        candidateConfiguration.Broker.Host.ShouldBe("candidate-broker.internal");
        candidateConfiguration.ClientId.ShouldBe("candidate-client");
        candidateConfiguration.Credentials.ShouldNotBeNull().Password.ShouldBe(candidateSecret);
        candidateConfiguration.Subscriptions.Keys.ShouldBe(["CandidateSubscription"], ignoreOrder: false);
        var candidateSession = defaultFactory.Sessions[1];
        candidateSession.ConnectCalls.ShouldBe(1);
        candidateSession.Subscribed.ShouldHaveSingleItem().Subscription.TopicFilter
            .ShouldBe("candidate/#");
        candidateSession.DisposeCalls.ShouldBe(1);
        initialSession.DisposeCalls.ShouldBe(0);

        var replaced = await host.Application.ApplyAsync(
            "keyed-replacement",
            CreateControllerRevisionDefinition(
                clientResource: "ReplacementClient",
                brokerHost: "replacement-broker.internal",
                clientId: "replacement-client",
                subscriptionResource: "ReplacementSubscription",
                topicFilter: "replacement/+",
                password: replacementSecret,
                maximumConcurrentRequests: 8));

        replaced.IsApplied.ShouldBeTrue();
        defaultFactory.Configurations.Count.ShouldBe(2);
        defaultFactory.Sessions.Count.ShouldBe(2);
        var replacementConfiguration = replacementFactory.Configurations.ShouldHaveSingleItem();
        replacementConfiguration.Name.ShouldBe("Resources.ReplacementClient");
        replacementConfiguration.Broker.Host.ShouldBe("replacement-broker.internal");
        replacementConfiguration.Broker.Host.ShouldNotBe("initial-broker.internal");
        replacementConfiguration.Broker.Host.ShouldNotBe("candidate-broker.internal");
        replacementConfiguration.ClientId.ShouldBe("replacement-client");
        replacementConfiguration.ClientId.ShouldNotBe("initial-client");
        replacementConfiguration.ClientId.ShouldNotBe("candidate-client");
        replacementConfiguration.Credentials.ShouldNotBeNull().Password.ShouldBe(replacementSecret);
        replacementConfiguration.Subscriptions.Keys.ShouldBe(["ReplacementSubscription"], ignoreOrder: false);
        replacementConfiguration.Subscriptions.ContainsKey("InitialSubscription").ShouldBeFalse();
        replacementConfiguration.Subscriptions.ContainsKey("CandidateSubscription").ShouldBeFalse();
        replacementConfiguration.Subscriptions["ReplacementSubscription"].TopicFilter
            .ShouldBe("replacement/+");
        var replacementSession = replacementFactory.Sessions.ShouldHaveSingleItem();
        replacementSession.ConnectCalls.ShouldBe(1);
        var replacementSubscription = replacementSession.Subscribed.ShouldHaveSingleItem();
        replacementSubscription.Identity.ShouldBe("name:ReplacementSubscription");
        replacementSubscription.Subscription.TopicFilter.ShouldBe("replacement/+");
        initialSession.DisposeCalls.ShouldBe(1);
        candidateSession.DisposeCalls.ShouldBe(1);
        replacementSession.DisposeCalls.ShouldBe(0);

        await host.Application.StopAsync();
        initialSession.DisposeCalls.ShouldBe(1);
        candidateSession.DisposeCalls.ShouldBe(1);
        replacementSession.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public void Obsolete_retry_resource_type_is_rejected_with_canonical_guidance()
    {
        var definition = Parse(CanonicalDefinitionJson.Replace(
            "\"retry.policy\"",
            "\"resilience.retry\"",
            StringComparison.Ordinal));
        var hostServices = new ServiceCollection()
            .AddSingleton<IMqttTransportFactory, UnusedTransportFactory>();
        hostServices.AddKeyedSingleton(
            "Resources.Messaging.Credentials",
            new MqttCredentialConfiguration { Password = "host-secret" });
        using var hostProvider = hostServices.BuildServiceProvider();
        var exception = Should.Throw<InvalidOperationException>(() =>
            RegisterResources(new ServiceCollection(), definition, hostProvider));

        exception.Message.ShouldContain("resilience.retry");
        exception.Message.ShouldContain("retry.policy");
    }

    [Fact]
    public async Task Inline_secret_material_requires_explicit_host_policy()
    {
        var definition = Parse("""
            {
              "Resources": {
                "Broker": { "Type": "mqtt.broker", "Host": "localhost" },
                "Client": {
                  "Type": "mqtt.client",
                  "ClientId": "inline-client",
                  "Broker": "Resources.Broker",
                  "Password": "inline-secret"
                }
              },
              "Workflows": {}
            }
            """);

        var hostServices = new ServiceCollection()
            .AddSingleton<IMqttTransportFactory, UnusedTransportFactory>();
        await using var hostProvider = hostServices.BuildServiceProvider();
        var services = new ServiceCollection();
        RegisterResources(services, definition, hostProvider);
        await using var provider = services.BuildServiceProvider();

        var error = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<MqttClientConfiguration>("Resources.Client"));
        error.Message.ShouldContain("did not approve", Case.Insensitive);
        error.Message.ShouldNotContain("inline-secret");
    }

    [Fact]
    public void Resource_validation_rejects_missing_and_wrong_type_references()
    {
        var missing = Parse("""
            {
              "Resources": {
                "Client": {
                  "Type": "mqtt.client",
                  "ClientId": "invalid",
                  "Broker": "Resources.Missing"
                }
              },
              "Workflows": {}
            }
            """);

        using var hostProvider = new ServiceCollection().BuildServiceProvider();
        Should.Throw<InvalidOperationException>(() =>
                RegisterResources(new ServiceCollection(), missing, hostProvider))
            .Message.ShouldContain("missing resource", Case.Insensitive);

        var wrongType = Parse("""
            {
              "Resources": {
                "Subscription": {
                  "Type": "mqtt.subscription",
                  "TopicFilter": "commands/#"
                },
                "Client": {
                  "Type": "mqtt.client",
                  "ClientId": "invalid",
                  "Broker": "Resources.Subscription"
                }
              },
              "Workflows": {}
            }
            """);

        Should.Throw<InvalidOperationException>(() =>
                RegisterResources(new ServiceCollection(), wrongType, hostProvider))
            .Message.ShouldContain("mqtt.broker", Case.Insensitive);
    }

    [Fact]
    public void Resource_validation_rejects_duplicate_subscription_leaf_names()
    {
        var definition = Parse("""
            {
              "Resources": {
                "Broker": { "Type": "mqtt.broker", "Host": "localhost" },
                "Primary": {
                  "Commands": {
                    "Type": "mqtt.subscription",
                    "TopicFilter": "commands/primary/#"
                  }
                },
                "Secondary": {
                  "Commands": {
                    "Type": "mqtt.subscription",
                    "TopicFilter": "commands/secondary/#"
                  }
                },
                "Client": {
                  "Type": "mqtt.client",
                  "ClientId": "duplicate-subscriptions",
                  "Broker": "Resources.Broker",
                  "Subscriptions": [
                    "Resources.Primary.Commands",
                    "Resources.Secondary.Commands"
                  ]
                }
              },
              "Workflows": {}
            }
            """);

        using var hostProvider = new ServiceCollection().BuildServiceProvider();
        var error = Should.Throw<InvalidOperationException>(() =>
            RegisterResources(new ServiceCollection(), definition, hostProvider));

        error.Message.ShouldContain("Resources.Client", Case.Sensitive);
        error.Message.ShouldContain("Commands", Case.Sensitive);
        error.Message.ShouldContain("unique", Case.Insensitive);
    }

    [Fact]
    public async Task Nested_resource_shapes_reject_unknown_properties()
    {
        var definition = Parse("""
            {
              "Resources": {
                "Broker": { "Type": "mqtt.broker", "Host": "localhost" },
                "Client": {
                  "Type": "mqtt.client",
                  "ClientId": "invalid",
                  "Broker": "Resources.Broker",
                  "Reconnect": { "InitialDelai": "00:00:01" }
                }
              },
              "Workflows": {}
            }
            """);
        var hostServices = new ServiceCollection()
            .AddSingleton<IMqttTransportFactory, UnusedTransportFactory>();
        await using var hostProvider = hostServices.BuildServiceProvider();
        var services = new ServiceCollection();
        RegisterResources(services, definition, hostProvider);
        await using var provider = services.BuildServiceProvider();

        var error = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<MqttClientConfiguration>("Resources.Client"));
        error.Message.ShouldContain("InitialDelai", Case.Sensitive);
        error.Message.ShouldContain("Reconnect", Case.Sensitive);
    }

    [Fact]
    public async Task Canonical_component_factories_share_host_owned_controller_and_expose_declared_ports()
    {
        var definition = Parse(CanonicalDefinitionJson);
        var controller = new RecordingController();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IMqttClientController>(ClientAddress, controller);
        await using var provider = services.BuildServiceProvider();
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddFluxFlowComponents().AddMqtt());
        var workflow = definition.Workflows["Main"];

        foreach (var (name, component) in workflow.Components)
        {
            var registration = registry.Components[component.Type];
            var composed = await registration.Factory(new ComponentActivationContext(
                provider,
                "Main",
                name,
                component));

            composed.Inputs.Keys.ShouldBe(registration.Inputs.Keys, ignoreOrder: false);
            composed.Outputs.Keys.ShouldBe(registration.Outputs.Keys, ignoreOrder: false);
            await composed.DisposeAsync();
        }

        controller.StartCalls.ShouldBe(4);
        controller.DisposeCalls.ShouldBe(0);
        await provider.DisposeAsync();
        controller.DisposeCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("\"Commands\"", false)]
    [InlineData("[\"Commands\",{\"TopicFilter\":\"alerts/#\",\"Qos\":\"AtLeastOnce\"}]", true)]
    public async Task Trigger_factory_maps_scalar_or_array_subscriptions_without_losing_options(
        string subscriptionJson,
        bool includesInlineSubscription)
    {
        var definition = Parse($$"""
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Trigger": {
                    "Type": "mqtt.receive",
                    "Client": "Resources.Client",
                    "Subscription": {{subscriptionJson}},
                    "WorkflowAcknowledgement": "Required",
                    "BrokerAcknowledgement": "AfterOutcome",
                    "OutcomeTimeout": "00:00:17",
                    "MaximumPendingMessages": 23
                  }
                }
              }
            }
            """);
        var controller = new RecordingController();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IMqttClientController>("Resources.Client", controller);
        await using var provider = services.BuildServiceProvider();
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddFluxFlowComponents().AddMqtt());
        var component = definition.Workflows["Main"].Components["Trigger"];

        var instance = await registry.Components[MqttComponentDefinition.Types.Trigger]
            .Factory(new ComponentActivationContext(
                provider,
                "Main",
                "Trigger",
                component));
        var optionsField = instance.Node.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(MqttSubscriptionTriggerOptions));
        var options = optionsField.GetValue(instance.Node)
            .ShouldBeOfType<MqttSubscriptionTriggerOptions>();

        options.TriggerId.ShouldBe("Main.Trigger");
        options.WorkflowAcknowledgement.ShouldBe(MqttWorkflowAcknowledgement.Required);
        options.BrokerAcknowledgement.ShouldBe(MqttBrokerAcknowledgement.AfterOutcome);
        options.OutcomeTimeout.ShouldBe(TimeSpan.FromSeconds(17));
        options.MaximumPendingMessages.ShouldBe(23);
        options.Subscriptions[0].Name.ShouldBe("Commands");
        if (includesInlineSubscription)
        {
            options.Subscriptions.Count.ShouldBe(2);
            options.Subscriptions[1].IsNamed.ShouldBeFalse();
            var inline = options.Subscriptions[1].Inline.ShouldNotBeNull();
            inline.TopicFilter.ShouldBe("alerts/#");
            inline.Qos.ShouldBe(MqttQos.AtLeastOnce);
        }
        else
        {
            options.Subscriptions.ShouldHaveSingleItem();
        }
        controller.StartCalls.ShouldBe(1);

        await instance.DisposeAsync();
    }

    private static readonly string CanonicalDefinitionJson = """
        {
          "Resources": {
            "Messaging": {
              "Broker": {
                "Type": "mqtt.broker",
                "Host": "broker.internal",
                "Port": 8883,
                "UseTls": true
              },
              "Retry": {
                "Type": "retry.policy",
                "Strategy": "Linear",
                "InitialDelay": "00:00:02"
              },
              "Commands": {
                "Type": "mqtt.subscription",
                "TopicFilter": "commands/+",
                "Qos": "AtLeastOnce"
              },
              "Alerts": {
                "Type": "mqtt.subscription",
                "TopicFilter": "alerts/#"
              },
              "Credentials": { "Type": "host.credentials" },
              "Client1": {
                "Type": "mqtt.client",
                "ClientId": "client-1",
                "Broker": "Resources.Messaging.Broker",
                "Credentials": "Resources.Messaging.Credentials",
                "Username": "direct-user",
                "Reconnect": "Resources.Messaging.Retry",
                "Subscriptions": "Resources.Messaging.Commands",
                "LastWill": {
                  "Topic": "clients/client-1/status",
                  "ContentBase64": "AAECAw==",
                  "ContentType": "application/octet-stream"
                }
              },
              "Client2": {
                "Type": "mqtt.client",
                "ClientId": "client-2",
                "Broker": "Resources.Messaging.Broker",
                "Reconnect": false,
                "Subscriptions": [
                  "Resources.Messaging.Commands",
                  "Resources.Messaging.Alerts"
                ]
              }
            }
          },
          "Workflows": {
            "Main": {
              "Control": {
                "Type": "mqtt.command",
                "Client": "Resources.Messaging.Client1",
                "RequestProcessing": "Concurrent",
                "MaximumConcurrentRequests": 4
              },
              "Publish": {
                "Type": "mqtt.publish",
                "Client": "Resources.Messaging.Client1",
                "MaximumPendingRequests": 64
              },
              "Trigger": {
                "Type": "mqtt.receive",
                "Client": "Resources.Messaging.Client1",
                "Subscription": "Commands",
                "Ack": "Control.Output",
                "Nak": "Publish.Output"
              },
              "Events": {
                "Type": "mqtt.events",
                "Client": "Resources.Messaging.Client1"
              }
            }
          }
        }
        """;

    private static ApplicationDefinition Parse(string json)
        => ApplicationDefinitionJson.Deserialize(json);

    private static ApplicationDefinition CreateEngineDefinition(int maximumConcurrentRequests)
        => Parse($$"""
            {
              "Resources": {
                "Broker": { "Type": "mqtt.broker", "Host": "localhost" },
                "Credentials": { "Type": "host.credentials" },
                "Client": {
                  "Type": "mqtt.client",
                  "ClientId": "engine-client",
                  "Broker": "Resources.Broker",
                  "Credentials": "Resources.Credentials"
                }
              },
              "Workflows": {
                "Main": {
                  "Control": {
                    "Type": "mqtt.command",
                    "Client": "Resources.Client",
                    "MaximumConcurrentRequests": {{maximumConcurrentRequests}}
                  }
                }
              }
            }
            """);

    private static ApplicationDefinition CreateControllerRevisionDefinition(
        string clientResource,
        string brokerHost,
        string clientId,
        string subscriptionResource,
        string topicFilter,
        string password,
        int maximumConcurrentRequests)
        => Parse($$"""
            {
              "Resources": {
                "Broker": {
                  "Type": "mqtt.broker",
                  "Host": "{{brokerHost}}"
                },
                "{{subscriptionResource}}": {
                  "Type": "mqtt.subscription",
                  "TopicFilter": "{{topicFilter}}"
                },
                "{{clientResource}}": {
                  "Type": "mqtt.client",
                  "ClientId": "{{clientId}}",
                  "Broker": "Resources.Broker",
                  "Password": "{{password}}",
                  "AutoConnect": "OnStart",
                  "Reconnect": false,
                  "Subscriptions": "Resources.{{subscriptionResource}}"
                }
              },
              "Workflows": {
                "Main": {
                  "Control": {
                    "Type": "mqtt.command",
                    "Client": "Resources.{{clientResource}}",
                    "MaximumConcurrentRequests": {{maximumConcurrentRequests}}
                  }
                }
              }
            }
            """);

    private static IServiceCollection RegisterResources(
        IServiceCollection services,
        ApplicationDefinition definition,
        IServiceProvider hostServices)
    {
        MqttCompositionResourceRegistrar.Register(services, definition, hostServices);
        return services;
    }

    private static IReadOnlyDictionary<string, ComponentDesignMetadata> DesignMetadataByType()
        => ComponentCatalogTestHost.CreateDesignMetadataCatalog(
                services => services.AddFluxFlowComponents().AddMqtt()).All
            .ToDictionary(metadata => metadata.Type.Value, StringComparer.Ordinal);

    private static void AssertMessagePort<T>(
        IReadOnlyDictionary<string, ComponentPortMetadata> ports,
        string name)
    {
        ports[name].Kind.ShouldBe(ComponentPortKind.Message);
        ports[name].MessageType.ShouldBe(typeof(T));
    }

    private static void AssertSignalPort(
        IReadOnlyDictionary<string, ComponentPortMetadata> ports,
        string name)
    {
        ports[name].Kind.ShouldBe(ComponentPortKind.Signal);
        ports[name].MessageType.ShouldBe(typeof(object));
    }

    private static void AssertOptionHints(
        ComponentDesignMetadata metadata,
        string optionName,
        string section,
        string importance,
        string editor)
    {
        var option = metadata.Options.Single(item => item.Name.Value == optionName);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(importance);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor)
            .ShouldBe(editor);
    }

    private static void AssertResourceHints(
        ResourceDesignMetadata resource,
        string pickerKind,
        string keyPattern)
    {
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
            .ShouldBe(pickerKind);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
            .ShouldBe(keyPattern);
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private sealed class RecordingController : IMqttClientController
    {
        private int _startCalls;
        private int _disposeCalls;

        public string Name => "recording";

        public bool IsConnected => false;

        public MqttTransportCapabilities Capabilities { get; } = new();

        public int StartCalls => Volatile.Read(ref _startCalls);

        public int DisposeCalls => Volatile.Read(ref _disposeCalls);

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _startCalls);
            return Task.CompletedTask;
        }

        public ValueTask<MqttClientResult> ExecuteAsync(
            MqttClientRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IMqttTriggerRegistration> RegisterTriggerAsync(
            MqttTriggerRegistrationOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IMqttClientEventSubscription> SubscribeEventsAsync(
            int capacity = 128,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCalls);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnusedTransportFactory : IMqttTransportFactory
    {
        public ValueTask<IMqttTransportSession> CreateAsync(
            MqttClientConfiguration configuration,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingTransportFactory : IMqttTransportFactory
    {
        private int _createCalls;

        public int CreateCalls => Volatile.Read(ref _createCalls);

        public List<MqttClientConfiguration> Configurations { get; } = [];

        public List<RecordingTransportSession> Sessions { get; } = [];

        public ValueTask<IMqttTransportSession> CreateAsync(
            MqttClientConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _createCalls);
            var session = new RecordingTransportSession();
            Configurations.Add(configuration);
            Sessions.Add(session);
            return ValueTask.FromResult<IMqttTransportSession>(session);
        }
    }

    private sealed class RecordingTransportSession : IMqttTransportSession
    {
        public MqttTransportCapabilities Capabilities { get; } = new();

        public bool IsConnected { get; private set; }

        public int ConnectCalls { get; private set; }

        public int DisconnectCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public List<(string Identity, MqttSubscriptionDefinition Subscription)> Subscribed { get; } = [];

        public IAsyncEnumerable<MqttTransportReceivedMessage> Messages
            => EmptyAsync<MqttTransportReceivedMessage>();

        public IAsyncEnumerable<MqttTransportEvent> Events
            => EmptyAsync<MqttTransportEvent>();

        public ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCalls++;
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisconnectCalls++;
            IsConnected = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAsync(
            MqttPublishMessage message,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask SubscribeAsync(
            string identity,
            MqttSubscriptionDefinition subscription,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Subscribed.Add((identity, subscription));
            return ValueTask.CompletedTask;
        }

        public ValueTask UnsubscribeAsync(
            string identity,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask AcknowledgeAsync(
            MqttTransportDeliveryToken delivery,
            MqttWorkflowOutcome outcome,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            IsConnected = false;
            return ValueTask.CompletedTask;
        }

        private static async IAsyncEnumerable<T> EmptyAsync<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingInlineSecretPolicy : IMqttInlineSecretPolicy
    {
        public List<(string Client, string Property)> Requests { get; } = [];

        public bool IsAllowed(ApplicationAddress client, string propertyName)
        {
            Requests.Add((client.Value, propertyName));
            return true;
        }
    }
}
