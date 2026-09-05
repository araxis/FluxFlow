using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace FluxFlow.Workflow.ContainerIntegrationTests;

public sealed class MqttBrokerFixture : IAsyncLifetime
{
    private const ushort MqttPort = 1883;
    private IContainer? _container;

    public string Host =>
        _container?.Hostname
        ?? throw new InvalidOperationException("The MQTT broker container has not started.");

    public int Port =>
        _container?.GetMappedPublicPort(MqttPort)
        ?? throw new InvalidOperationException("The MQTT broker container has not started.");

    public async Task InitializeAsync()
    {
        const string configuration = """
            listener 1883
            allow_anonymous true
            persistence false
            """;
        var container = new ContainerBuilder("eclipse-mosquitto:2.0.22")
            .WithPortBinding(MqttPort, assignRandomHostPort: true)
            .WithResourceMapping(
                Encoding.UTF8.GetBytes(configuration),
                FilePath.Of("/mosquitto/config/mosquitto.conf"))
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(MqttPort)
                    .UntilExternalTcpPortIsAvailable(MqttPort))
            .Build();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            await container.StartAsync(timeout.Token);
            _container = container;
        }
        catch (Exception exception)
        {
            var (stdout, stderr) = await container.GetLogsAsync();
            await container.DisposeAsync();
            throw new InvalidOperationException(
                $"The MQTT broker container did not become ready.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{stderr}",
                exception);
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
