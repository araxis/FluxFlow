using FluxFlow.Environments.Testcontainers;
using FluxFlow.Execution;
using FluxFlow.Execution.Plugins;
using FluxFlow.Plugins;
using FluxFlow.Studio.UI;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class BrokerEnvironmentPluginTests
{
    [Theory]
    [InlineData("mqtt.client", "mosquitto", "rabbitmq-mqtt")]
    [InlineData("amqp.connection", "rabbitmq-amqp")]
    [InlineData("nats.connection", "nats")]
    public void Only_installed_plugins_offer_their_editors_and_managed_services(string type, params string[] names)
    {
        (IFluxFlowPlugin Module, IPluginDesign Design) family = type switch
        {
            "mqtt.client" => (new FluxFlow.Plugins.Mqtt.MqttPlugin(), new FluxFlow.Plugins.Mqtt.MqttDesign()),
            "amqp.connection" => (new FluxFlow.Plugins.RabbitMQ.RabbitMQPlugin(), new FluxFlow.Plugins.RabbitMQ.RabbitDesign()),
            _ => (new FluxFlow.Plugins.Nats.NatsPlugin(), new FluxFlow.Plugins.Nats.NatsDesign())
        };
        var configuration = System.Text.Json.JsonSerializer.Serialize(new { Workflows = new { main = new { } }, Resources = new { broker = new { Type = type } } });
        foreach (var active in new[] { true, false })
        {
            using var plugins = active ? new InstalledPlugins(family.Module) : new InstalledPlugins();
            var registrations = new ServiceCollection().AddStudioUI(plugins.Descriptions);
            if (active) family.Design.ConfigureRemoteServices(registrations);
            using var studio = registrations.BuildServiceProvider();
            using var runner = new ServiceCollection().AddLocalFlowRunner(plugins).AddFlowContainers().BuildServiceProvider();
            var editors = studio.GetServices<EnvironmentProviderDescription>().ToArray();
            editors.Select(editor => editor.Name).Order().ShouldBe(active ? names.Order().ToArray() : []);
            editors.ShouldAllBe(editor => editor.ResourceType == type && editor.StartupFiles.Count > 0);
            runner.GetServices<ContainerServiceDefinition>().Select(container => container.Provider.Name).Order()
                .ShouldBe(editors.Select(editor => editor.Name).Order());
            var preparation = runner.GetRequiredService<EnvironmentPreparation>();
            foreach (var name in names)
            {
                var profile = new EnvironmentProfile { Resources = new() { ["Resources:broker"] = new() { Provider = name } } };
                if (active) preparation.Validate(configuration, profile);
                else Should.Throw<ArgumentException>(() => preparation.Validate(configuration, profile));
            }
        }
    }
}
