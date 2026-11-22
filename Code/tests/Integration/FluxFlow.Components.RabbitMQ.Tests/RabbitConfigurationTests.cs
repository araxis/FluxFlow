using System.Text;
using FluxFlow.Configuration.Components.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RabbitMQ.Tests;

public sealed class RabbitConfigurationTests
{
    [Fact]
    public void Feedback_policy_defaults_to_reject_and_preserves_explicit_requeue()
    {
        using var defaults = Read("""{"Type":"amqp.consumer","Connection":"broker","Queue":"q"}""");
        RabbitComponentConfiguration.Read(defaults.GetSection("Workflows:main:consumer")).ConsumerOptions().RequeueOnNack.ShouldBeFalse();
        using var explicitPolicy = Read("""{"Type":"amqp.consumer","Connection":"broker","Queue":"q","RequeueOnNack":true,"ACK":"done.Output","NACK":"failed.Output"}""");
        var configuration = RabbitComponentConfiguration.Read(explicitPolicy.GetSection("Workflows:main:consumer"));
        configuration.ConsumerOptions().RequeueOnNack.ShouldBeTrue();
        configuration.ACK.Single().Address.Value.ShouldBe("main.done.Output");
        configuration.NACK.Single().Address.Value.ShouldBe("main.failed.Output");
    }

    [Fact]
    public void Explicit_field_types_survive_configuration_binding()
    {
        using var configuration = Read("""
            {"Type":"amqp.consumer","Connection":"broker","Queue":"readings","Topology":{
              "Queues":[{"Name":"readings","Arguments":{"x-message-ttl":{"Type":"Int32","Value":30000}}}],
              "Exchanges":[{"Name":"events","Type":"headers"}],
              "Bindings":[{"Queue":"readings","Exchange":"events","Arguments":{
                "x-match":{"Type":"String","Value":"all"},"id":{"Type":"Int64","Value":"9223372036854775807"},
                "bytes":{"Type":"Bytes","Value":"AP8="},"text":{"Type":"String","Value":"123"},
                "array":{"Type":"Array","Value":[{"Type":"Boolean","Value":true}]}}}]}}
            """);
        var config = RabbitComponentConfiguration.Read(configuration.GetSection("Workflows:main:consumer"));
        config.Validate(true);
        config.Topology.Queues.Single().Arguments!["x-message-ttl"].ToNative().ShouldBe(30000);
        var arguments = config.Topology.Bindings.Single().Arguments!;
        arguments["id"].ToNative().ShouldBe(long.MaxValue);
        arguments["text"].ToNative().ShouldBe("123");
        ((byte[])arguments["bytes"].ToNative()!).ShouldBe(new byte[] { 0, 255 });
        ((IList<object?>)arguments["array"].ToNative()!).Single().ShouldBe(true);
    }
    [Theory]
    [InlineData("amqp://user:password@localhost")]
    [InlineData("http://localhost")]
    [InlineData("amqp://localhost/vhost")]
    [InlineData("amqp://localhost:0")]
    public void Connection_rejects_invalid_or_secret_bearing_endpoints(string endpoint)
        => Should.Throw<ArgumentException>(() => new RabbitConnectionConfiguration { Endpoint = endpoint }.Validate());

    [Fact]
    public void Validation_does_not_resolve_host_credentials()
    {
        new RabbitConnectionConfiguration { UsernameEnvironmentVariable = "UNDEFINED_AMQP_USER", PasswordEnvironmentVariable = "UNDEFINED_AMQP_PASSWORD" }.Validate();
        Should.Throw<ArgumentException>(() => new RabbitConnectionConfiguration { UsernameEnvironmentVariable = "USER_ONLY" }.Validate());
    }
    [Fact]
    public void No_topology_means_no_declarations()
    {
        using var configuration = Read("""{"Type":"amqp.consumer","Connection":"broker","Queue":"existing"}""");
        var config = RabbitComponentConfiguration.Read(configuration.GetSection("Workflows:main:consumer"));
        config.Validate(true);
        config.Topology.Queues.ShouldBeEmpty(); config.Topology.Exchanges.ShouldBeEmpty(); config.Topology.Bindings.ShouldBeEmpty();
    }
    [Fact]
    public void Empty_declaration_lists_round_trip_through_json_configuration()
    {
        using var configuration = Read("""{"Type":"amqp.consumer","Connection":"broker","Queue":"q","Topology":{"Exchanges":[],"Queues":[],"Bindings":[]}}""");
        var config = RabbitComponentConfiguration.Read(configuration.GetSection("Workflows:main:consumer"));
        config.Validate(true);
        config.Topology.Exchanges.ShouldBeEmpty(); config.Topology.Queues.ShouldBeEmpty(); config.Topology.Bindings.ShouldBeEmpty();
    }
    [Theory]
    [InlineData("Boolean")]
    [InlineData("Int32")]
    public void Missing_typed_argument_values_are_not_silently_defaulted(string type)
    {
        using var configuration = Read("{\"Type\":\"amqp.consumer\",\"Connection\":\"broker\",\"Queue\":\"q\",\"Topology\":{\"Queues\":[{\"Name\":\"q\",\"Arguments\":{\"flag\":{\"Type\":\"" + type + "\"}}}]}}");
        Should.Throw<ArgumentException>(() => RabbitComponentConfiguration.Read(configuration.GetSection("Workflows:main:consumer")));
    }
    private static ConfigurationRoot Read(string json) => (ConfigurationRoot)new ConfigurationBuilder()
        .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("{\"Workflows\":{\"main\":{\"consumer\":" + json + "}}}"))).Build();
}
