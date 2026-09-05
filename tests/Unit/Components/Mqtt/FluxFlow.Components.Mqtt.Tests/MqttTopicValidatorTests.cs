using FluxFlow.Components.Mqtt.Validation;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttTopicValidatorTests
{
    [Theory]
    [InlineData("devices/state")]
    [InlineData("/devices/state")]
    [InlineData("devices/state/")]
    public void ValidatePublishTopic_AcceptsConcreteTopic(string topic)
    {
        var result = MqttTopicValidator.ValidatePublishTopic(topic);

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("devices/+")]
    [InlineData("devices/#")]
    [InlineData("devices/\0/state")]
    public void ValidatePublishTopic_RejectsInvalidTopic(string? topic)
    {
        var result = MqttTopicValidator.ValidatePublishTopic(topic);

        result.IsValid.ShouldBeFalse();
        result.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatePublishTopic_RejectsOversizedTopic()
    {
        var result = MqttTopicValidator.ValidatePublishTopic(new string('a', 65_536));

        result.IsValid.ShouldBeFalse();
        result.Message!.ShouldContain("65535");
    }

    [Theory]
    [InlineData("devices/+")]
    [InlineData("devices/#")]
    [InlineData("+/state")]
    [InlineData("#")]
    public void ValidateSubscriptionFilter_AcceptsValidFilter(string topicFilter)
    {
        var result = MqttTopicValidator.ValidateSubscriptionFilter(topicFilter);

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("devices/#/state")]
    [InlineData("devices/temperature#")]
    [InlineData("devices/+/state+")]
    [InlineData("devices/\0/state")]
    public void ValidateSubscriptionFilter_RejectsInvalidFilter(string? topicFilter)
    {
        var result = MqttTopicValidator.ValidateSubscriptionFilter(topicFilter);

        result.IsValid.ShouldBeFalse();
        result.Message.ShouldNotBeNullOrWhiteSpace();
    }
}
