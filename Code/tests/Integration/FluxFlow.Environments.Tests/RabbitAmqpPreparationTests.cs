using System.Text.Json.Nodes;
using FluxFlow.Environments.Testcontainers;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class RabbitAmqpPreparationTests
{
    [Fact]
    public void Managed_credentials_and_virtual_host_replace_only_the_run_snapshot()
    {
        var original = JsonNode.Parse("""{"Endpoint":"amqps://production:5671","VirtualHost":"production","UsernameEnvironmentVariable":"PRODUCTION_USER","PasswordEnvironmentVariable":"PRODUCTION_PASSWORD","TimeoutSeconds":17}""")!.AsObject();
        var saved = original.ToJsonString();
        var resource = original.DeepClone().AsObject();
        var adapter = new RabbitMqEnvironmentAdapter();
        var endpoint = new Uri("amqp://localhost:54321");
        adapter.Apply(resource, endpoint, new());
        resource.ContainsKey("UsernameEnvironmentVariable").ShouldBeFalse();
        resource.ContainsKey("PasswordEnvironmentVariable").ShouldBeFalse();
        resource["VirtualHost"]!.GetValue<string>().ShouldBe("/");
        resource["TimeoutSeconds"]!.GetValue<int>().ShouldBe(17);
        var binding = new ResourceBinding { Provider = "rabbitmq-amqp", Variables = new()
        {
            ["RABBITMQ_DEFAULT_USER_ENVIRONMENT_VARIABLE"] = "TRIAL_USER",
            ["RABBITMQ_DEFAULT_PASS_ENVIRONMENT_VARIABLE"] = "TRIAL_PASSWORD",
            ["RABBITMQ_DEFAULT_VHOST"] = "trial"
        } };
        new ConfiguredContainerProvider(FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpContainer.Definition).Validate(binding);
        adapter.Apply(resource, endpoint, binding);
        resource["UsernameEnvironmentVariable"]!.GetValue<string>().ShouldBe("TRIAL_USER");
        resource["PasswordEnvironmentVariable"]!.GetValue<string>().ShouldBe("TRIAL_PASSWORD");
        resource["VirtualHost"]!.GetValue<string>().ShouldBe("trial");
        var existing = original.DeepClone().AsObject();
        adapter.ApplyExisting(existing, new Uri("amqps://another-host:5671"));
        existing["UsernameEnvironmentVariable"]!.GetValue<string>().ShouldBe("PRODUCTION_USER");
        existing["VirtualHost"]!.GetValue<string>().ShouldBe("production");
        original.ToJsonString().ShouldBe(saved);
    }

    [Theory]
    [InlineData("RABBITMQ_DEFAULT_USER", "inline")]
    [InlineData("RABBITMQ_DEFAULT_PASS", "inline")]
    [InlineData("RABBITMQ_DEFAULT_VHOST", " ")]
    [InlineData("RABBITMQ_DEFAULT_USER_ENVIRONMENT_VARIABLE", "MISSING_PAIR")]
    public void Invalid_or_inline_broker_credentials_are_rejected_without_starting_containers(string key, string value)
    {
        var binding = new ResourceBinding { Provider = "rabbitmq-amqp", Variables = new() { [key] = value } };
        Should.Throw<ArgumentException>(() => new ConfiguredContainerProvider(FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpContainer.Definition).Validate(binding));
    }
}
