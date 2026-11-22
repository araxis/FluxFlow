using System.Text;
using FluxFlow.Configuration.Components.Nats;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Nats.Tests;

public sealed class JetStreamConfigurationTests
{
    [Theory]
    [InlineData("bad.name")]
    [InlineData("bad name")]
    [InlineData("*")]
    [InlineData("bad/name")]
    public void Invalid_stream_names_are_rejected(string name) => Should.Throw<ArgumentException>(() =>
        new JetStreamConfiguration { Connection = "broker", Stream = name }.StreamOptions());

    [Fact]
    public void Provisioning_defaults_are_opt_in_and_bounds_validate_before_execution()
    {
        var existing = new JetStreamConfiguration { Connection = "broker", Stream = "READINGS", Consumer = "worker" };
        existing.ConsumerOptions().CreateConsumer.ShouldBeFalse(); existing.StreamOptions().CreateStream.ShouldBeFalse();
        Should.Throw<ArgumentException>(() => (existing with { CreateStream = true, MaxStreamMessages = 0 }).StreamOptions());
        Should.Throw<ArgumentException>(() => (existing with { CreateConsumer = true, MaxDeliver = 0 }).ConsumerOptions());
        Should.Throw<ArgumentException>(() => (existing with { MaxInFlight = 0 }).ConsumerOptions());
        Should.Throw<ArgumentException>(() => (existing with { CreateConsumer = true, AckWaitSeconds = 0 }).ConsumerOptions());
        Should.Throw<ArgumentException>(() => (existing with { CreateConsumer = true, FilterSubject = "bad.>.suffix" }).ConsumerOptions());
        Should.Throw<ArgumentException>(() => new JetStreamSettlementConfiguration { Consumer = "worker", Capacity = 0 }.Validate());
        // New-entity settings do not silently redefine an existing consumer.
        (existing with { MaxDeliver = 0, AckWaitSeconds = 0 }).ConsumerOptions().CreateConsumer.ShouldBeFalse();
    }

    [Fact]
    public void Visual_json_settings_bind_to_the_same_explicit_options()
    {
        using var settings = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("""
            {"Connection":"broker","Stream":"READINGS","Consumer":"worker","CreateStream":true,"CreateConsumer":true,
             "StreamSubject":"sensors.>","Storage":"Memory","DeliverPolicy":"New","MaxInFlight":4,"MaxDeliver":7,"AckWaitSeconds":15}
            """))).Build();
        var config = settings.Get<JetStreamConfiguration>()!;
        var options = config.ConsumerOptions(); options.Stream.StreamSubject.ShouldBe("sensors.>");
        options.Stream.Storage.ToString().ShouldBe("Memory"); options.DeliverPolicy.ToString().ShouldBe("New");
        options.MaxInFlight.ShouldBe(4); options.MaxDeliver.ShouldBe(7); options.AckWaitSeconds.ShouldBe(15);
    }
}
