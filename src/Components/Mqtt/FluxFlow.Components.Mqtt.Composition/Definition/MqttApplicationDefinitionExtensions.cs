using FluxFlow.Composition;
using FluxFlow.Composition.Definition;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttResources
{
    internal static IApplicationResourceRegistrar Registrar { get; } =
        new MqttCompositionResourceRegistrar();

    public static ApplicationResourceContract<MqttBrokerResourceBuilder, MqttBrokerResourceHandle> Broker { get; } =
        ApplicationResourceContract.Create(
            MqttComponentDefinition.ResourceTypes.Broker,
            Registrar,
            static () => new MqttBrokerResourceBuilder(),
            static (options, definition) => options.Apply(definition),
            static resource => new MqttBrokerResourceHandle(resource));

    public static ApplicationResourceContract<MqttRetryPolicyResourceBuilder, MqttRetryPolicyResourceHandle> RetryPolicy { get; } =
        ApplicationResourceContract.Create(
            MqttComponentDefinition.ResourceTypes.Retry,
            Registrar,
            static () => new MqttRetryPolicyResourceBuilder(),
            static (options, definition) => options.Apply(definition),
            static resource => new MqttRetryPolicyResourceHandle(resource));

    public static ApplicationResourceContract<MqttSubscriptionResourceBuilder, MqttSubscriptionResourceHandle> Subscription { get; } =
        ApplicationResourceContract.Create(
            MqttComponentDefinition.ResourceTypes.Subscription,
            Registrar,
            static () => new MqttSubscriptionResourceBuilder(),
            static (options, definition) => options.Apply(definition),
            static resource => new MqttSubscriptionResourceHandle(resource));

    public static ApplicationResourceContract<MqttClientResourceBuilder, MqttClientResourceHandle> Client { get; } =
        ApplicationResourceContract.Create(
            MqttComponentDefinition.ResourceTypes.Client,
            Registrar,
            static () => new MqttClientResourceBuilder(),
            static (options, definition) => options.Apply(definition),
            static resource => new MqttClientResourceHandle(resource));
}

public static class MqttApplicationDefinitionExtensions
{
    public static MqttBrokerResourceHandle AddMqttBroker(
        this ApplicationDefinitionBuilder resources,
        string name,
        Action<MqttBrokerResourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(configure);
        return resources.AddResource(name, MqttResources.Broker, configure);
    }

    public static ApplicationDefinitionBuilder AddMqttBroker(
        this ApplicationDefinitionBuilder resources,
        string name,
        Action<MqttBrokerResourceBuilder> configure,
        out MqttBrokerResourceHandle broker)
    {
        broker = resources.AddMqttBroker(name, configure);
        return resources;
    }

    public static MqttRetryPolicyResourceHandle AddMqttRetryPolicy(
        this ApplicationDefinitionBuilder resources,
        string name,
        Action<MqttRetryPolicyResourceBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return resources.AddResource(
            name,
            MqttResources.RetryPolicy,
            configure ?? (static _ => { }));
    }

    public static ApplicationDefinitionBuilder AddMqttRetryPolicy(
        this ApplicationDefinitionBuilder resources,
        string name,
        out MqttRetryPolicyResourceHandle retryPolicy)
    {
        retryPolicy = resources.AddMqttRetryPolicy(name);
        return resources;
    }

    public static ApplicationDefinitionBuilder AddMqttRetryPolicy(
        this ApplicationDefinitionBuilder resources,
        string name,
        Action<MqttRetryPolicyResourceBuilder> configure,
        out MqttRetryPolicyResourceHandle retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(configure);
        retryPolicy = resources.AddMqttRetryPolicy(name, configure);
        return resources;
    }

    public static MqttSubscriptionResourceHandle AddMqttSubscription(
        this ApplicationDefinitionBuilder resources,
        string name,
        Action<MqttSubscriptionResourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(configure);
        return resources.AddResource(name, MqttResources.Subscription, configure);
    }

    public static ApplicationDefinitionBuilder AddMqttSubscription(
        this ApplicationDefinitionBuilder resources,
        string name,
        Action<MqttSubscriptionResourceBuilder> configure,
        out MqttSubscriptionResourceHandle subscription)
    {
        subscription = resources.AddMqttSubscription(name, configure);
        return resources;
    }

    public static MqttClientResourceHandle AddMqttClient(
        this ApplicationDefinitionBuilder resources,
        string name,
        Action<MqttClientResourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(configure);
        return resources.AddResource(name, MqttResources.Client, configure);
    }

    public static ApplicationDefinitionBuilder AddMqttClient(
        this ApplicationDefinitionBuilder resources,
        string name,
        Action<MqttClientResourceBuilder> configure,
        out MqttClientResourceHandle client)
    {
        client = resources.AddMqttClient(name, configure);
        return resources;
    }

    public static MqttCommandHandle AddMqttCommand(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MqttCommandBuilder> configure)
        => workflow.AddComponent(name, MqttComponentDefinition.Types.Control,
            MqttCompositionNodeFactories.CreateControlNodeAsync,
            definition =>
            {
                var options = new MqttCommandBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new MqttCommandHandle(component));

    public static WorkflowDefinitionBuilder AddMqttCommand(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MqttCommandBuilder> configure,
        out MqttCommandHandle command)
    {
        command = workflow.AddMqttCommand(name, configure);
        return workflow;
    }

    public static MqttPublishHandle AddMqttPublish(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MqttPublishBuilder> configure)
        => workflow.AddComponent(name, MqttComponentDefinition.Types.Publish,
            MqttCompositionNodeFactories.CreatePublishNodeAsync,
            definition =>
            {
                var options = new MqttPublishBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new MqttPublishHandle(component));

    public static WorkflowDefinitionBuilder AddMqttPublish(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MqttPublishBuilder> configure,
        out MqttPublishHandle publish)
    {
        publish = workflow.AddMqttPublish(name, configure);
        return workflow;
    }

    public static MqttReceiveHandle AddMqttReceive(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MqttReceiveBuilder> configure)
        => workflow.AddComponent(name, MqttComponentDefinition.Types.Trigger,
            MqttCompositionNodeFactories.CreateTriggerNodeAsync,
            definition =>
            {
                var options = new MqttReceiveBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new MqttReceiveHandle(component));

    public static WorkflowDefinitionBuilder AddMqttReceive(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MqttReceiveBuilder> configure,
        out MqttReceiveHandle receive)
    {
        receive = workflow.AddMqttReceive(name, configure);
        return workflow;
    }

    public static MqttEventsHandle AddMqttEvents(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MqttEventsBuilder> configure)
        => workflow.AddComponent(name, MqttComponentDefinition.Types.Events,
            MqttCompositionNodeFactories.CreateEventsNodeAsync,
            definition =>
            {
                var options = new MqttEventsBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new MqttEventsHandle(component));

    public static WorkflowDefinitionBuilder AddMqttEvents(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MqttEventsBuilder> configure,
        out MqttEventsHandle events)
    {
        events = workflow.AddMqttEvents(name, configure);
        return workflow;
    }
}

