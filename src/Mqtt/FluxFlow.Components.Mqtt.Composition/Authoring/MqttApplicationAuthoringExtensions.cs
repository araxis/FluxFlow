using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttApplicationAuthoringExtensions
{
    public static MqttBrokerResourceHandle AddMqttBroker(
        this IResourceDefinitionContainerBuilder resources,
        string name,
        Action<MqttBrokerResourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(configure);
        var handle = resources.AddResource(
            name,
            MqttComponentDefinition.ResourceTypes.Broker,
            definition =>
            {
                var builder = new MqttBrokerResourceBuilder();
                configure(builder);
                builder.Apply(definition);
            });
        return new MqttBrokerResourceHandle(handle);
    }

    public static TResources AddMqttBroker<TResources>(
        this TResources resources,
        string name,
        Action<MqttBrokerResourceBuilder> configure,
        out MqttBrokerResourceHandle broker)
        where TResources : IResourceDefinitionContainerBuilder
    {
        broker = resources.AddMqttBroker(name, configure);
        return resources;
    }

    public static MqttRetryPolicyResourceHandle AddMqttRetryPolicy(
        this IResourceDefinitionContainerBuilder resources,
        string name,
        Action<MqttRetryPolicyResourceBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var handle = resources.AddResource(
            name,
            MqttComponentDefinition.ResourceTypes.Retry,
            definition =>
            {
                var builder = new MqttRetryPolicyResourceBuilder();
                configure?.Invoke(builder);
                builder.Apply(definition);
            });
        return new MqttRetryPolicyResourceHandle(handle);
    }

    public static TResources AddMqttRetryPolicy<TResources>(
        this TResources resources,
        string name,
        out MqttRetryPolicyResourceHandle retryPolicy)
        where TResources : IResourceDefinitionContainerBuilder
    {
        retryPolicy = resources.AddMqttRetryPolicy(name);
        return resources;
    }

    public static TResources AddMqttRetryPolicy<TResources>(
        this TResources resources,
        string name,
        Action<MqttRetryPolicyResourceBuilder> configure,
        out MqttRetryPolicyResourceHandle retryPolicy)
        where TResources : IResourceDefinitionContainerBuilder
    {
        ArgumentNullException.ThrowIfNull(configure);
        retryPolicy = resources.AddMqttRetryPolicy(name, configure);
        return resources;
    }

    public static MqttSubscriptionResourceHandle AddMqttSubscription(
        this IResourceDefinitionContainerBuilder resources,
        string name,
        Action<MqttSubscriptionResourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(configure);
        var handle = resources.AddResource(
            name,
            MqttComponentDefinition.ResourceTypes.Subscription,
            definition =>
            {
                var builder = new MqttSubscriptionResourceBuilder();
                configure(builder);
                builder.Apply(definition);
            });
        return new MqttSubscriptionResourceHandle(handle);
    }

    public static TResources AddMqttSubscription<TResources>(
        this TResources resources,
        string name,
        Action<MqttSubscriptionResourceBuilder> configure,
        out MqttSubscriptionResourceHandle subscription)
        where TResources : IResourceDefinitionContainerBuilder
    {
        subscription = resources.AddMqttSubscription(name, configure);
        return resources;
    }

    public static MqttClientResourceHandle AddMqttClient(
        this IResourceDefinitionContainerBuilder resources,
        string name,
        Action<MqttClientResourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(configure);
        var handle = resources.AddResource(
            name,
            MqttComponentDefinition.ResourceTypes.Client,
            definition =>
            {
                var builder = new MqttClientResourceBuilder();
                configure(builder);
                builder.Apply(definition);
            });
        return new MqttClientResourceHandle(handle);
    }

    public static TResources AddMqttClient<TResources>(
        this TResources resources,
        string name,
        Action<MqttClientResourceBuilder> configure,
        out MqttClientResourceHandle client)
        where TResources : IResourceDefinitionContainerBuilder
    {
        client = resources.AddMqttClient(name, configure);
        return resources;
    }

    public static MqttCommandHandle AddMqttCommand(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MqttCommandBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var handle = workflow.AddComponent(
            name,
            MqttComponentDefinition.Types.Control,
            definition =>
            {
                var builder = new MqttCommandBuilder();
                configure(builder);
                builder.Apply(definition);
            });
        return new MqttCommandHandle(handle);
    }

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
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var handle = workflow.AddComponent(
            name,
            MqttComponentDefinition.Types.Publish,
            definition =>
            {
                var builder = new MqttPublishBuilder();
                configure(builder);
                builder.Apply(definition);
            });
        return new MqttPublishHandle(handle);
    }

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
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var handle = workflow.AddComponent(
            name,
            MqttComponentDefinition.Types.Trigger,
            definition =>
            {
                var builder = new MqttReceiveBuilder();
                configure(builder);
                builder.Apply(definition);
            });
        return new MqttReceiveHandle(handle);
    }

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
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var handle = workflow.AddComponent(
            name,
            MqttComponentDefinition.Types.Events,
            definition =>
            {
                var builder = new MqttEventsBuilder();
                configure(builder);
                builder.Apply(definition);
            });
        return new MqttEventsHandle(handle);
    }

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
