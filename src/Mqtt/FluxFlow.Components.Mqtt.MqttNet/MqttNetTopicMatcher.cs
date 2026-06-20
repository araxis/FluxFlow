using MQTTnet;

namespace FluxFlow.Components.Mqtt.MqttNet;

internal static class MqttNetTopicMatcher
{
    public static bool IsMatch(string topicFilter, string topic)
        => MqttTopicFilterComparer.Compare(topic, topicFilter) ==
           MqttTopicFilterCompareResult.IsMatch;
}
