using Rcl;

namespace FluxFlow.Configuration.Components.Ros2;

/// <summary>Creates topic options without resolving ROS nodes or message types.</summary>
public static class RosConfigurationOptions
{
    public static PublisherOptions CreateOptions(this RosPublisherConfiguration config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Node);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Topic);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.OutputCapacity);
        return new(qos: config.Qos);
    }

    public static SubscriptionOptions CreateOptions(this RosSubscriberConfiguration config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Node);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Topic);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.OutputCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.QueueSize);
        if (!Enum.IsDefined(config.FullMode)) throw new ArgumentOutOfRangeException(nameof(config.FullMode));
        return new(qos: config.Qos, queueSize: config.QueueSize, fullMode: config.FullMode,
            ignoreLocalPublications: config.IgnoreLocalPublications);
    }
}
