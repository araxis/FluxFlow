using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
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
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Composition.Tests;

public sealed class MqttCompositionNodeRegistryExtensionsTests
{
    private const string ClientAddress = "Resources.Messaging.Client1";

    [Fact]
    public void RegisterMqttNodes_registers_four_vnext_contracts()
    {
        var registry = new CompositionNodeRegistry().RegisterMqttNodes();

        registry.Registrations.Keys.ShouldBe([
            MqttCompositionNodeTypes.Control,
            MqttCompositionNodeTypes.Publish,
            MqttCompositionNodeTypes.Trigger,
            MqttCompositionNodeTypes.Events
        ], ignoreOrder: false);
        registry.TryResolveResourceType(
            MqttCompositionResourceTypes.LegacyRetry,
            out var canonicalRetryType).ShouldBeTrue();
        canonicalRetryType.ShouldBe(MqttCompositionResourceTypes.Retry);

        AssertMessagePort<MqttClientRequest>(
            registry.Registrations[MqttCompositionNodeTypes.Control].Inputs,
            MqttCompositionPortNames.Input);
        AssertMessagePort<MqttClientResult>(
            registry.Registrations[MqttCompositionNodeTypes.Control].Outputs,
            MqttCompositionPortNames.Output);
        AssertMessagePort<MqttPublishMessage>(
            registry.Registrations[MqttCompositionNodeTypes.Publish].Inputs,
            MqttCompositionPortNames.Input);
        AssertMessagePort<MqttClientResult>(
            registry.Registrations[MqttCompositionNodeTypes.Publish].Outputs,
            MqttCompositionPortNames.Output);

        var trigger = registry.Registrations[MqttCompositionNodeTypes.Trigger];
        AssertSignalPort(trigger.Inputs, MqttCompositionPortNames.Ack);
        AssertSignalPort(trigger.Inputs, MqttCompositionPortNames.Nak);
        AssertMessagePort<MqttReceivedApplicationMessage>(
            trigger.Outputs,
            MqttCompositionPortNames.Output);

        var events = registry.Registrations[MqttCompositionNodeTypes.Events];
        events.Inputs.ShouldBeEmpty();
        AssertMessagePort<MqttClientEvent>(events.Outputs, MqttCompositionPortNames.Output);

        registry.TryGetRegistration(MqttCompositionNodeTypes.LegacyControl, out var controlAlias)
            .ShouldBeTrue();
        controlAlias.ShouldBeSameAs(registry.Registrations[MqttCompositionNodeTypes.Control]);
        registry.TryGetRegistration(MqttCompositionNodeTypes.LegacyTrigger, out var triggerAlias)
            .ShouldBeTrue();
        triggerAlias.ShouldBeSameAs(trigger);
    }

    [Fact]
    public void Design_metadata_describes_four_nodes_shared_client_and_signal_inputs()
    {
        var metadata = DesignMetadataByType();

        metadata.Keys.ShouldBe([
            MqttCompositionNodeTypes.Control,
            MqttCompositionNodeTypes.Publish,
            MqttCompositionNodeTypes.Trigger,
            MqttCompositionNodeTypes.Events
        ], ignoreOrder: false);

        foreach (var item in metadata.Values)
        {
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Category.ShouldBe(new ComponentCategory("MQTT"));

            var client = item.Resources.Single(resource =>
                resource.Name.Value == MqttCompositionResourceNames.Client);
            client.IsRequired.ShouldBeTrue();
            client.ValueType?.Value.ShouldBe(nameof(IMqttClientController));
            AssertResourceHints(
                client,
                ResourceDesignMetadataAttributeValues.Client,
                "Resources.{name}");
        }

        var trigger = metadata[MqttCompositionNodeTypes.Trigger];
        trigger.Ports.Single(port => port.Name.Value == MqttCompositionPortNames.Ack)
            .Attributes[new ComponentAttributeName(PortDesignMetadataAttributeNames.Kind)]
            .Value.ShouldBe(PortDesignMetadataAttributeValues.Signal);
        trigger.Ports.Single(port => port.Name.Value == MqttCompositionPortNames.Nak)
            .Attributes[new ComponentAttributeName(PortDesignMetadataAttributeNames.Kind)]
            .Value.ShouldBe(PortDesignMetadataAttributeValues.Signal);

        var clock = trigger.Resources.Single(resource =>
            resource.Name.Value == MqttCompositionResourceNames.Clock);
        clock.IsRequired.ShouldBeFalse();
        AssertResourceHints(
            clock,
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");

        AssertOptionHints(
            metadata[MqttCompositionNodeTypes.Control],
            "maximumConcurrentRequests",
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            metadata[MqttCompositionNodeTypes.Trigger],
            "subscription",
            "Subscription",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(
            metadata[MqttCompositionNodeTypes.Events],
            "maximumPendingEvents",
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public async Task Canonical_resources_bind_nested_addresses_shared_broker_and_scalar_or_array_subscriptions()
    {
        var definition = Parse(CanonicalDefinitionJson);
        var services = new ServiceCollection()
            .AddSingleton<IMqttTransportFactory, UnusedTransportFactory>();
        services.AddKeyedSingleton(
            "Resources.Messaging.Credentials",
            new MqttCredentialConfiguration
            {
                Username = "referenced-user",
                Password = "host-secret"
            });
        services.AddMqttCompositionResources(definition);

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
        first.LastWill!.Content.OriginalBytes.ToArray().ShouldBe([0, 1, 2, 3]);

        var firstController = provider.GetRequiredKeyedService<IMqttClientController>(ClientAddress);
        provider.GetRequiredKeyedService<IMqttClientController>(ClientAddress)
            .ShouldBeSameAs(firstController);
        provider.GetRequiredKeyedService<IMqttClientController>("Resources.Messaging.Client2")
            .ShouldNotBeSameAs(firstController);
    }

    [Fact]
    public async Task Legacy_retry_resource_type_remains_loadable()
    {
        var definition = Parse(CanonicalDefinitionJson.Replace(
            "\"retry.policy\"",
            "\"resilience.retry\"",
            StringComparison.Ordinal));
        var services = new ServiceCollection()
            .AddSingleton<IMqttTransportFactory, UnusedTransportFactory>();
        services.AddKeyedSingleton(
            "Resources.Messaging.Credentials",
            new MqttCredentialConfiguration { Password = "host-secret" });
        services.AddMqttCompositionResources(definition);

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<MqttClientConfiguration>(ClientAddress)
            .Reconnect.Policy.Strategy.ShouldBe(MqttRetryStrategy.Linear);
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

        var services = new ServiceCollection()
            .AddSingleton<IMqttTransportFactory, UnusedTransportFactory>();
        services.AddMqttCompositionResources(definition);
        await using var provider = services.BuildServiceProvider();

        var error = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<MqttClientConfiguration>("Resources.Client"));
        error.Message.ShouldContain("did not approve", Case.Insensitive);
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

        Should.Throw<InvalidOperationException>(() =>
                new ServiceCollection().AddMqttCompositionResources(missing))
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
                new ServiceCollection().AddMqttCompositionResources(wrongType))
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

        var error = Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddMqttCompositionResources(definition));

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
        var services = new ServiceCollection()
            .AddSingleton<IMqttTransportFactory, UnusedTransportFactory>();
        services.AddMqttCompositionResources(definition);
        await using var provider = services.BuildServiceProvider();

        var error = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<MqttClientConfiguration>("Resources.Client"));
        error.Message.ShouldContain("InitialDelai", Case.Sensitive);
        error.Message.ShouldContain("Reconnect", Case.Sensitive);
    }

    [Fact]
    public async Task Canonical_component_factories_share_controller_and_expose_declared_ports()
    {
        var definition = Parse(CanonicalDefinitionJson);
        var controller = new RecordingController();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IMqttClientController>(ClientAddress, controller);
        await using var provider = services.BuildServiceProvider();
        var registry = new CompositionNodeRegistry().RegisterMqttNodes();
        var workflow = definition.Workflows["Main"];

        foreach (var (name, component) in workflow.Components)
        {
            var registration = registry.Registrations[component.Type];
            var composed = await registration.Factory(new CompositionNodeFactoryContext(
                provider,
                "Main",
                name,
                component));

            composed.Inputs.Keys.ShouldBe(registration.Inputs.Keys, ignoreOrder: false);
            composed.Outputs.Keys.ShouldBe(registration.Outputs.Keys, ignoreOrder: false);
            await composed.DisposeAsync();
        }

        controller.StartCalls.ShouldBe(4);
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

    private static IReadOnlyDictionary<string, ComponentDesignMetadata> DesignMetadataByType()
        => new MqttComponentDesignMetadataProvider()
            .GetMetadata()
            .ToDictionary(metadata => metadata.Type.Value, StringComparer.Ordinal);

    private static void AssertMessagePort<T>(
        IReadOnlyDictionary<string, CompositionPortMetadata> ports,
        string name)
    {
        ports[name].Kind.ShouldBe(CompositionPortKind.Message);
        ports[name].MessageType.ShouldBe(typeof(T));
    }

    private static void AssertSignalPort(
        IReadOnlyDictionary<string, CompositionPortMetadata> ports,
        string name)
    {
        ports[name].Kind.ShouldBe(CompositionPortKind.Signal);
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

        public string Name => "recording";

        public bool IsConnected => false;

        public MqttTransportCapabilities Capabilities { get; } = new();

        public int StartCalls => Volatile.Read(ref _startCalls);

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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UnusedTransportFactory : IMqttTransportFactory
    {
        public ValueTask<IMqttTransportSession> CreateAsync(
            MqttClientConfiguration configuration,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
