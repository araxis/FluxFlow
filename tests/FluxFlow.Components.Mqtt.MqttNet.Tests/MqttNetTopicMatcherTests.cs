using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.MqttNet.Tests;

public sealed class MqttNetTopicMatcherTests
{
    [Theory]
    [InlineData("devices/a", "devices/a")]
    [InlineData("devices/+", "devices/a")]
    [InlineData("devices/+/state", "devices/a/state")]
    [InlineData("devices/#", "devices/a/state")]
    [InlineData("devices//state", "devices//state")]
    [InlineData("$SYS/+", "$SYS/broker")]
    public void IsMatch_ReturnsTrueForMatchingFilters(string filter, string topic)
        => MqttNetTopicMatcher.IsMatch(filter, topic).ShouldBeTrue();

    [Theory]
    [InlineData("devices/a", "devices/b")]
    [InlineData("devices/+", "devices/a/state")]
    [InlineData("devices/+/state", "devices/a")]
    [InlineData("devices/#", "sensor/a")]
    [InlineData("+/broker", "$SYS/broker")]
    [InlineData("", "devices/a")]
    [InlineData("devices/+", "")]
    public void IsMatch_ReturnsFalseForNonMatchingFilters(string filter, string topic)
        => MqttNetTopicMatcher.IsMatch(filter, topic).ShouldBeFalse();
}
