using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Mqtt.Composition;

public abstract class MqttComponentBuilder
{
    private ResourceHandle<IMqttClientController>? _externalClient;

    public MqttClientResourceHandle? Client { get; set; }

    public void UseClient(ResourceHandle<IMqttClientController> client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _externalClient = client;
    }

    private protected void ApplyClient(ComponentDefinitionBuilder definition)
    {
        if (Client is not null && _externalClient is not null)
            throw new InvalidOperationException("MQTT component Client cannot be both an MQTT client resource and an external client resource.");
        if (Client is null && _externalClient is null)
            throw new InvalidOperationException("MQTT components require Client.");
        definition.UseResource(
            MqttComponentDefinition.Resources.Client,
            Client?.Definition ?? _externalClient!);
    }

    private protected static void SetIfPresent<T>(
        ComponentDefinitionBuilder definition,
        string name,
        T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class MqttCommandBuilder : MqttComponentBuilder
{
    public MqttRequestProcessing? RequestProcessing { get; set; }
    public MqttResultOrder? ResultOrder { get; set; }
    public int? MaximumConcurrentRequests { get; set; }
    public int? MaximumPendingRequests { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyClient(definition);
        SetIfPresent(definition, MqttComponentDefinition.Options.RequestProcessing, RequestProcessing);
        SetIfPresent(definition, MqttComponentDefinition.Options.ResultOrder, ResultOrder);
        SetIfPresent(definition, MqttComponentDefinition.Options.MaximumConcurrentRequests, MaximumConcurrentRequests);
        SetIfPresent(definition, MqttComponentDefinition.Options.MaximumPendingRequests, MaximumPendingRequests);
    }
}

public sealed class MqttPublishBuilder : MqttComponentBuilder
{
    public int? MaximumPendingRequests { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyClient(definition);
        SetIfPresent(definition, MqttComponentDefinition.Options.MaximumPendingRequests, MaximumPendingRequests);
    }
}

public sealed class MqttReceiveBuilder : MqttComponentBuilder
{
    private readonly List<MqttSubscriptionTarget> _subscriptions = [];

    public MqttWorkflowAcknowledgement? WorkflowAcknowledgement { get; set; }
    public MqttBrokerAcknowledgement? BrokerAcknowledgement { get; set; }
    public TimeSpan? OutcomeTimeout { get; set; }
    public int? MaximumPendingMessages { get; set; }
    public ResourceHandle? Clock { get; set; }

    public void AddSubscription(MqttSubscriptionResourceHandle subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _subscriptions.Add(MqttSubscriptionTarget.Named(subscription.Name));
    }

    public void AddSubscription(MqttSubscriptionDefinition subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _subscriptions.Add(MqttSubscriptionTarget.FromInline(subscription));
    }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyClient(definition);
        if (_subscriptions.Count == 0)
            throw new InvalidOperationException("MQTT receive components require at least one subscription.");

        if (_subscriptions.Count == 1)
        {
            definition.Set(
                MqttComponentDefinition.Options.Subscription,
                _subscriptions[0]);
        }
        else
        {
            definition.Set(
                MqttComponentDefinition.Options.Subscription,
                _subscriptions);
        }
        SetIfPresent(definition, MqttComponentDefinition.Options.WorkflowAcknowledgement, WorkflowAcknowledgement);
        SetIfPresent(definition, MqttComponentDefinition.Options.BrokerAcknowledgement, BrokerAcknowledgement);
        SetIfPresent(definition, MqttComponentDefinition.Options.OutcomeTimeout, OutcomeTimeout);
        SetIfPresent(definition, MqttComponentDefinition.Options.MaximumPendingMessages, MaximumPendingMessages);
        if (Clock is not null)
            definition.UseResource(MqttComponentDefinition.Resources.Clock, Clock);
    }
}

public sealed class MqttEventsBuilder : MqttComponentBuilder
{
    public int? MaximumPendingEvents { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyClient(definition);
        SetIfPresent(definition, MqttComponentDefinition.Options.MaximumPendingEvents, MaximumPendingEvents);
    }
}

public abstract class MqttComponentHandle
{
    private protected MqttComponentHandle(ComponentHandle definition)
    {
        Definition = definition;
    }

    protected ComponentHandle Definition { get; }

    public ApplicationAddress Address => Definition.Address;

    public string Name => Definition.Name;

    public override string ToString() => Address.Value;
}

public sealed class MqttCommandHandle : MqttComponentHandle
{
    internal MqttCommandHandle(ComponentHandle definition) : base(definition)
    {
        Input = definition.Input<MqttClientRequest>(MqttComponentDefinition.Ports.Input);
        Output = definition.Output<MqttClientResult>(MqttComponentDefinition.Ports.Output);
    }

    public InputPortHandle<MqttClientRequest> Input { get; }
    public OutputPortHandle<MqttClientResult> Output { get; }
}

public sealed class MqttPublishHandle : MqttComponentHandle
{
    internal MqttPublishHandle(ComponentHandle definition) : base(definition)
    {
        Input = definition.Input<MqttPublishMessage>(MqttComponentDefinition.Ports.Input);
        Output = definition.Output<MqttClientResult>(MqttComponentDefinition.Ports.Output);
    }

    public InputPortHandle<MqttPublishMessage> Input { get; }
    public OutputPortHandle<MqttClientResult> Output { get; }
}

public sealed class MqttReceiveHandle : MqttComponentHandle
{
    internal MqttReceiveHandle(ComponentHandle definition) : base(definition)
    {
        Ack = definition.SignalInput(MqttComponentDefinition.Ports.Ack);
        Nak = definition.SignalInput(MqttComponentDefinition.Ports.Nak);
        Output = definition.Output<MqttReceivedApplicationMessage>(MqttComponentDefinition.Ports.Output);
    }

    public SignalInputPortHandle Ack { get; }
    public SignalInputPortHandle Nak { get; }
    public OutputPortHandle<MqttReceivedApplicationMessage> Output { get; }
}

public sealed class MqttEventsHandle : MqttComponentHandle
{
    internal MqttEventsHandle(ComponentHandle definition) : base(definition)
    {
        Output = definition.Output<MqttClientEvent>(MqttComponentDefinition.Ports.Output);
    }

    public OutputPortHandle<MqttClientEvent> Output { get; }
}
