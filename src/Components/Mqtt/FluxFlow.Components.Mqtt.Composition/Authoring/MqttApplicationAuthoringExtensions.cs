using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Components.Designer;

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

public static class MqttComponents
{
    public static ComponentContract<MqttCommandBuilder, MqttCommandHandle> MqttCommand { get; } =
        DesignedComponentContract.Create(
            MqttComponentDefinition.Types.Control,
            MqttServiceCollectionExtensions.ConfigureControl,
            static () => new MqttCommandBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new MqttCommandHandle(component));

    public static ComponentContract<MqttPublishBuilder, MqttPublishHandle> MqttPublish { get; } =
        DesignedComponentContract.Create(
            MqttComponentDefinition.Types.Publish,
            MqttServiceCollectionExtensions.ConfigurePublish,
            static () => new MqttPublishBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new MqttPublishHandle(component));

    public static ComponentContract<MqttReceiveBuilder, MqttReceiveHandle> MqttReceive { get; } =
        DesignedComponentContract.Create(
            MqttComponentDefinition.Types.Trigger,
            MqttServiceCollectionExtensions.ConfigureTrigger,
            static () => new MqttReceiveBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new MqttReceiveHandle(component));

    public static ComponentContract<MqttEventsBuilder, MqttEventsHandle> MqttEvents { get; } =
        DesignedComponentContract.Create(
            MqttComponentDefinition.Types.Events,
            MqttServiceCollectionExtensions.ConfigureEvents,
            static () => new MqttEventsBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new MqttEventsHandle(component));
}

public static class MqttApplicationAuthoringExtensions
{
    public static MqttBrokerResourceHandle AddMqttBroker(
        this IResourceDefinitionContainerBuilder resources,
        string name,
        Action<MqttBrokerResourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(configure);
        return resources.AddResource(name, MqttResources.Broker, configure);
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
        return resources.AddResource(
            name,
            MqttResources.RetryPolicy,
            configure ?? (static _ => { }));
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
        return resources.AddResource(name, MqttResources.Subscription, configure);
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
        return resources.AddResource(name, MqttResources.Client, configure);
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
        => workflow.AddComponent(name, MqttComponents.MqttCommand, configure);

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
        => workflow.AddComponent(name, MqttComponents.MqttPublish, configure);

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
        => workflow.AddComponent(name, MqttComponents.MqttReceive, configure);

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
        => workflow.AddComponent(name, MqttComponents.MqttEvents, configure);

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
