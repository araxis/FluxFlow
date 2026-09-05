namespace FluxFlow.Components.Mqtt.Subscriptions;

internal static class MqttTopicFilterMatcher
{
    public static bool IsMatch(string topic, string filter)
    {
        if (string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(filter))
            return false;

        var topicLevels = topic.Split('/');
        var filterLevels = filter.Split('/');
        var topicIndex = 0;

        for (var filterIndex = 0; filterIndex < filterLevels.Length; filterIndex++)
        {
            var filterLevel = filterLevels[filterIndex];
            if (filterLevel == "#")
                return filterIndex == filterLevels.Length - 1;

            if (topicIndex >= topicLevels.Length)
                return false;

            if (filterLevel != "+" &&
                !string.Equals(filterLevel, topicLevels[topicIndex], StringComparison.Ordinal))
            {
                return false;
            }

            topicIndex++;
        }

        return topicIndex == topicLevels.Length;
    }
}
