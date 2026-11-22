using System.Diagnostics;
using System.Text.Json.Nodes;
using FluxFlow.Environments.Testcontainers;
using FluxFlow.Execution;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions;
using FluxFlow.DesignerApp.Services;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Environments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

[Collection("Containers")]
[Trait("Category", "Docker")]
public sealed partial class ContainerRunTests
{
    [Fact]
    public async Task Mqtt_pending_limit_fault_can_stop_cleanly_and_rerun_with_original_errors_retained()
    {
        const string configuration = """
            {"Resources":{"broker":{"Type":"mqtt.client","Host":"localhost","ClientId":"pending-limit-trial"}},"Workflows":{"main":{
              "connection":{"Type":"mqtt.connection","Connection":"broker"},
              "receive":{"Type":"mqtt.subscriber","Connection":"broker","Topics":[{"Topic":"trial/work","QoS":1}],
                "AcknowledgementMode":"Manual","MaxPendingAcknowledgements":2,"ACK":"settle.Output","Output":"reply.Input"},
              "reply":{"Type":"requestReply","MaxInFlight":1,"BufferCapacity":2,"ReplyTimeout":"00:00:01","TimeoutBehavior":"Continue",
                "Request":{"Address":"worker.Input","Engine":"jsonata","Condition":"Text = 'ack'"},"ACK":"worker.Output",
                "Output":{"Address":"settle.Input","Engine":"jsonata","Condition":"Outcome = 'ACK'"}},
              "worker":{"Type":"mapper","Engine":"jsonata","Expression":"$"},
              "settle":{"Type":"mapper","Engine":"jsonata","Expression":"{'AcknowledgementId': Request.AcknowledgementId}"}
            }}}
            """;
        var graph = CreateGraph(configuration);
        var beforeSetup = graph.ToProjectJson();
        graph.SetEnvironment(new() { Resources = new() { ["Resources:broker"] = new() { Provider = "mosquitto" } } });
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var app = await CreateCompanion(new ConfiguredContainerProvider(FluxFlow.Plugins.Mqtt.MosquittoContainer.Definition));
        using var client = app.GetTestClient();
        var runner = new HttpEnvironmentRunner(client);
        await using var session = new StudioRunSession(runner);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        EnvironmentRunStatus? stopped = null;
        string? retained = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await session.StartAsync(graph).WaitAsync(deadline.Token);
            var started = session.Status!;
            started.State.ShouldBe("Running", started.Error);
            var service = started.Services.Single();
            var endpoint = new Uri(service.Endpoint);
            using var publisher = new MqttClientFactory().CreateMqttClient();
            await publisher.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(endpoint.Host, endpoint.Port).Build(), deadline.Token);
            try
            {
                await WaitFor(status => status.Outputs!.Records.Any(record => record.Port == "main.receive.Events"
                    && record.Message?.GetRawText().Contains("mqtt.subscriptions_applied", StringComparison.Ordinal) == true));
                for (var index = 0; index < 3; index++)
                {
                    await publisher.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("trial/work")
                        .WithPayload(attempt == 0 ? "hold" : "ack").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(), deadline.Token);
                    if (attempt == 1)
                        await WaitFor(status => status.Outputs!.Records.Count(record => record.Port == "main.receive.Events"
                            && record.Message?.GetRawText().Contains("mqtt.acknowledged", StringComparison.Ordinal) == true) == index + 1);
                }
                if (attempt == 0)
                {
                    var failed = await WaitFor(status => status.State == "Failed");
                    failed.Error.ShouldNotBeNull();
                    failed.Error.ShouldContain("pending acknowledgement limit");
                    failed.Outputs!.Records.Count(record => record.Port == "main.receive.Output").ShouldBe(2);
                }
            }
            finally { await session.StopAsync(); }
            session.CleanupError.ShouldBeNull();
            var final = await runner.GetAsync(started.Id, deadline.Token);
            final.State.ShouldBe("Stopped");
            await runner.StopAsync(started.Id, deadline.Token);
            await AssertRemoved(service.ContainerId!);
            if (attempt == 0)
            {
                final.Diagnostics!.Records.ShouldContain(record => record.Message == "Workflow execution failed."
                    && record.Exception != null && record.Exception.Contains("pending acknowledgement limit", StringComparison.Ordinal));
                final.Diagnostics.Records.ShouldNotContain(record => record.Message == "Run resource cleanup failed.");
                stopped = final;
                retained = System.Text.Json.JsonSerializer.Serialize(final);
            }
            else
            {
                System.Text.Json.JsonSerializer.Serialize(await runner.GetAsync(stopped!.Id, deadline.Token)).ShouldBe(retained);
                final.Outputs!.Records.Where(record => record.Port == "main.receive.Output")
                    .ShouldAllBe(record => record.Message!.Value.GetProperty("Value").GetProperty("Text").GetString() == "ack");
            }

            async Task<EnvironmentRunStatus> WaitFor(Func<EnvironmentRunStatus, bool> predicate)
            {
                while (true)
                {
                    var status = await runner.GetAsync(started.Id, deadline.Token);
                    if (predicate(status)) return status;
                    await Task.Delay(25, deadline.Token);
                }
            }
        }
        AssertHistory(graph, beforeSetup, project, revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RabbitMQ_execution_logs_survive_transport_shutdown_and_unexpected_disconnect(bool disconnect)
    {
        var observer = new OriginalLogObserver();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.AddProvider(observer);
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.Debug);
        builder.Services.AddLocalFlowRunner(RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Mqtt.MqttPlugin(), new FluxFlow.Plugins.Http.HttpPlugin()));
        builder.Services.AddFlowContainers();
        await using var app = builder.Build();
        app.MapLocalFlowRunner();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var runner = new HttpEnvironmentRunner(client);
        await using var session = new StudioRunSession(runner);
        var graph = CreateGraph(Configuration);
        graph.SetEnvironment(BrokerProfile(true));
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        EnvironmentRunStatus? started = null;
        try
        {
            await session.StartAsync(graph).WaitAsync(timeout.Token);
            started = session.Status!;
            started.State.ShouldBe("Running", started.Error);
            var brokerService = started.Services.Single(item => item.Provider == "rabbitmq-mqtt");
            var broker = new Uri(brokerService.Endpoint);
            using var publisher = new MqttClientFactory().CreateMqttClient();
            await publisher.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(broker.Host, broker.Port).Build(), timeout.Token);
            // The existing sample's logger emits an unchanged diagnostic for invalid input.
            const string mappingFailure = "Telemetry failure node.processing_failed:";
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (!session.Logs.Records.Any(item => item.Message.StartsWith(mappingFailure, StringComparison.Ordinal)) && DateTimeOffset.UtcNow < deadline)
            {
                await publisher.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("sensors/log-contract/temperature")
                    .WithPayload("invalid-temperature").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(), timeout.Token);
                await Task.Delay(200, timeout.Token);
            }
            session.Logs.Records.ShouldContain(item => item.Message.StartsWith(mappingFailure, StringComparison.Ordinal),
                string.Join("\n", observer.Records.Where(item => !item.Category.StartsWith("Microsoft.AspNetCore")).Select(item => $"{item.Category}: {item.Message}")));
            var live = session.Logs.Records.Last(item => item.Message.StartsWith(mappingFailure, StringComparison.Ordinal));
            live.Level.ShouldBe("Error");
            live.Category.ShouldNotBeNullOrWhiteSpace();
            if (disconnect)
            {
                await ReadDockerOutput(timeout.Token, "stop", "--time", "0", brokerService.ContainerId!);
                const string connectionFailure = "Telemetry failure mqtt.connection_failed:";
                deadline = DateTimeOffset.UtcNow.AddSeconds(15);
                while (!session.Logs.Records.Any(item => item.Sequence > live.Sequence && item.Message.StartsWith(connectionFailure, StringComparison.Ordinal)) && DateTimeOffset.UtcNow < deadline)
                    await Task.Delay(50, timeout.Token);
                session.Logs.Records.ShouldContain(item => item.Sequence > live.Sequence && item.Message.StartsWith(connectionFailure, StringComparison.Ordinal),
                    string.Join("\n", observer.Records.Select(item => $"{item.Category}: {item.Message}")));
            }
        }
        finally { await session.StopAsync(); }
        session.CleanupError.ShouldBeNull();
        session.LogReadError.ShouldBeNull();
        var final = await runner.GetAsync(session.Status!.Id, timeout.Token);
        final.State.ShouldBe("Stopped");
        final.Logs!.Records.ShouldNotBeEmpty();
        System.Text.Json.JsonSerializer.Serialize(session.Logs, EnvironmentJsonContext.Default.ExecutionLogSnapshot)
            .ShouldBe(System.Text.Json.JsonSerializer.Serialize(final.Logs, EnvironmentJsonContext.Default.ExecutionLogSnapshot));
        final.Logs.Records.ShouldContain(item => item.Category == "Microsoft.Extensions.Hosting.Internal.Host" && item.EventId == 4);
        foreach (var record in final.Logs.Records)
            observer.Records.ShouldContain(new OriginalLog(record.Level, record.Category, record.EventId, record.EventName, record.Message, record.Exception));
        System.Text.Json.JsonSerializer.Serialize((await runner.GetAsync(final.Id, timeout.Token)).Logs, EnvironmentJsonContext.Default.ExecutionLogSnapshot)
            .ShouldBe(System.Text.Json.JsonSerializer.Serialize(final.Logs, EnvironmentJsonContext.Default.ExecutionLogSnapshot));
        foreach (var service in started!.Services) await AssertRemoved(service.ContainerId!);
        graph.ToProjectJson().ShouldBe(project);
        graph.Revision.ShouldBe(revision);
    }

    private sealed record OriginalLog(string Level, string Category, int EventId, string? EventName, string Message, string? Exception);

    private sealed class OriginalLogObserver : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentBag<OriginalLog> Records { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);
        public void Dispose() { }
        private sealed class Recorder(OriginalLogObserver owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Records.Add(new(level.ToString(), category, eventId.Id, eventId.Name, formatter(state, exception), exception?.ToString()));
        }
    }

    [Fact]
    public async Task Environment_check_pings_Docker_without_creating_containers_or_changing_setup()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var before = await ReadDockerOutput(timeout.Token, "ps", "--all", "--quiet", "--no-trunc");
        var request = new EnvironmentRunRequest(Configuration, BrokerProfile(true, authenticated: true));
        var original = System.Text.Json.JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        var check = new EnvironmentCheck(PreparationTests.Create(new ConfiguredContainerProvider(FluxFlow.Plugins.Mqtt.RabbitMqMqttContainer.Definition), new ConfiguredContainerProvider(FluxFlow.Plugins.Http.WireMockContainer.Definition)), [new DockerRuntimeCheck()]);
        var result = await check.CheckAsync(request, timeout.Token);
        result.Success.ShouldBeTrue(result.Message);
        (await ReadDockerOutput(timeout.Token, "ps", "--all", "--quiet", "--no-trunc")).ShouldBe(before);
        System.Text.Json.JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest).ShouldBe(original);
    }

    private static string Configuration => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "telemetry.json")))!["FluxFlow"]!.ToJsonString();
    private const string Mappings = """
        [{"Request":{"Path":"/readings","Methods":["post"]},"Response":{"StatusCode":201,"Body":"accepted"}}]
        """;
    private static EnvironmentProfile Profile => new() { Resources = new()
    {
        ["Resources:broker"] = new() { Provider = "mosquitto", Image = FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider.Image, PullIfMissing = false },
        ["Resources:api"] = new() { Provider = "wiremock", Image = FluxFlow.Plugins.Http.WireMock.WireMockMappings.Provider.Image, PullIfMissing = false, Mappings = Mappings,
            Files = new() { ["/app/trial.txt"] = "project-specific setup" }, Variables = new() { ["TRIAL_NAME"] = "telemetry" } }
    } };

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false, 2)]
    [InlineData(true, false, false, 2)]
    [InlineData(false, false, true, 2, true)]
    [InlineData(true, false, true, 2, true)]
    [InlineData(false, false, false, 1, true, true)]
    public async Task Studio_runs_telemetry_through_the_companion_and_stops_without_changing_project_history(bool rabbitMq, bool customStartup, bool authenticated, int instanceCount = 1, bool topologyBindings = false, bool httpOnly = false)
    {
        var source = Configuration;
        if (topologyBindings)
        {
            var config = JsonNode.Parse(source)!;
            if (!httpOnly)
            {
                config["Resources"]!["boundBroker"] = config["Resources"]!["broker"]!.DeepClone();
                config["Resources"]!["boundBroker"]!["Address"] = "mqtt://127.0.0.1:1";
                config["Workflows"]!["telemetry"]!["connection"]!["Connection"] = "boundBroker";
                config["Workflows"]!["telemetry"]!["receive"]!["Connection"] = "boundBroker";
            }
            config["Resources"]!["boundApi"] = config["Resources"]!["api"]!.DeepClone();
            config["Resources"]!["boundApi"]!["BaseAddress"] = "http://127.0.0.1:1/";
            config["Workflows"]!["telemetry"]!["send"]!["Client"] = "boundApi";
            source = config.ToJsonString();
        }
        var mqtt = Broker(rabbitMq);
        await using var app = await CreateCompanion(mqtt, new ConfiguredContainerProvider(FluxFlow.Plugins.Http.WireMockContainer.Definition));
        using var companionClient = app.GetTestClient();
        var runner = new HttpEnvironmentRunner(companionClient);
        await using var session = new StudioRunSession(runner);
        var graph = CreateGraph(source);
        if (instanceCount > 1 || topologyBindings)
        {
            graph.SetTopology(new() { Nodes = Enumerable.Range(1, instanceCount).Select(i => new EnvironmentNode
                { Id = $"instance-{i}", Name = $"Application {i}", Kind = EnvironmentNodeKind.Application }).ToList() });
            graph.ResetHistory();
        }
        if (topologyBindings)
        {
            var topology = graph.GetEnvironmentDefinition().Topology;
            foreach (var instance in topology.Nodes)
            {
                if (!httpOnly) instance.Bindings["Resources:boundBroker"] = "broker-service";
                instance.Bindings["Resources:boundApi"] = "api-service";
            }
            topology.Nodes.Add(new() { Id = "broker-service", Name = "Broker", Kind = EnvironmentNodeKind.Service, Resource = "Resources:broker" });
            topology.Nodes.Add(new() { Id = "api-service", Name = "API", Kind = EnvironmentNodeKind.Service, Resource = "Resources:api" });
            graph.SetTopology(topology);
            graph.ResetHistory();
        }
        var beforeSetup = graph.ToProjectJson();
        var environment = BrokerProfile(rabbitMq, authenticated);
        if (customStartup)
            environment.Resources["Resources:broker"] = environment.Resources["Resources:broker"] with { Files = new()
            {
                ["/etc/rabbitmq/enabled_plugins"] = "[rabbitmq_mqtt,rabbitmq_management].\n",
                ["/etc/rabbitmq/rabbitmq.conf"] = "mqtt.listeners.tcp.default = 1883\nmqtt.allow_anonymous = true\nloopback_users.guest = false\nlog.console = false\nlog.file = /tmp/rabbitmq.log\n"
            } };
        var registry = new ImageRegistry();
        var preset = new ContainerProfile { Id = "p2", Name = "P2", RegistryId = registry.Id, Provider = "wiremock", SetupLocked = true,
            Settings = environment.Resources["Resources:api"] };
        environment.Resources["Resources:api"] = preset.CreateBinding([registry]);
        graph.SetEnvironment(environment);
        graph.GetEnvironment().Resources["Resources:api"].Profile!.SetupLocked.ShouldBeTrue();
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        EnvironmentRunStatus? status = null;
        try
        {
            await session.StartAsync(graph).WaitAsync(timeout.Token);
            status = session.Status!;
            status.Error.ShouldBeNull();
            status.State.ShouldBe("Running");
            var brokerService = status.Services.Single(item => item.Provider == mqtt.Name);
            var broker = new Uri(brokerService.Endpoint);
            if (customStartup)
                foreach (var (path, contents) in environment.Resources["Resources:broker"].Files)
                    (await ReadContainer(status.Services.Single(item => item.Provider == mqtt.Name).ContainerId!, timeout.Token, "cat", path)).ShouldBe(contents);
            var api = status.Services.Single(item => item.Provider == "wiremock");
            if (httpOnly)
            {
                var clientId = JsonNode.Parse(source)!["Resources"]!["broker"]!["ClientId"]!.GetValue<string>();
                (await ReadDockerOutput(timeout.Token, "logs", brokerService.ContainerId!)).ShouldContain($" as {clientId} (");
            }
            (await ReadContainer(api.ContainerId!, timeout.Token, "cat", "/app/trial.txt")).ShouldBe("project-specific setup");
            (await ReadContainer(api.ContainerId!, timeout.Token, "printenv", "TRIAL_NAME")).Trim().ShouldBe("telemetry");
            using var publisher = new MqttClientFactory().CreateMqttClient();
            var publisherOptions = new MqttClientOptionsBuilder().WithTcpServer(broker.Host, broker.Port);
            if (authenticated)
            {
                using var anonymous = new MqttClientFactory().CreateMqttClient();
                (await anonymous.ConnectAsync(publisherOptions.Build(), timeout.Token)).ResultCode.ShouldBeOneOf(
                    MqttClientConnectResultCode.BadUserNameOrPassword, MqttClientConnectResultCode.NotAuthorized);
                anonymous.IsConnected.ShouldBeFalse();
                using var wrongPassword = new MqttClientFactory().CreateMqttClient();
                (await wrongPassword.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(broker.Host, broker.Port)
                    .WithCredentials("reader", "incorrect-test-password").Build(), timeout.Token)).ResultCode.ShouldBeOneOf(
                        MqttClientConnectResultCode.BadUserNameOrPassword, MqttClientConnectResultCode.NotAuthorized);
                wrongPassword.IsConnected.ShouldBeFalse();
                publisherOptions.WithCredentials("publisher", "publisher-test-password").WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500);
                using var forbidden = new MqttClientFactory().CreateMqttClient();
                (await forbidden.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(broker.Host, broker.Port)
                    .WithCredentials("reader", "reader-test-password").WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500).Build(), timeout.Token))
                    .ResultCode.ShouldBe(MqttClientConnectResultCode.Success);
                await AssertPublishDenied(forbidden, "sensors/forbidden/temperature", rabbitMq, brokerService.ContainerId!, timeout.Token);
            }
            (await publisher.ConnectAsync(publisherOptions.Build(), timeout.Token)).ResultCode.ShouldBe(MqttClientConnectResultCode.Success);
            (await publisher.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("sensors/trial-device/temperature")
                .WithPayload("23.5").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).WithRetainFlag().Build(), timeout.Token))
                .ReasonCode.ShouldBe(MqttClientPublishReasonCode.Success);
            using var client = new HttpClient { BaseAddress = new Uri(api.Endpoint) };
            JsonObject? received = null;
            while (received is null)
            {
                var requests = JsonNode.Parse(await client.GetStringAsync("/__admin/requests", timeout.Token))!.AsArray();
                received = requests.OfType<JsonObject>().FirstOrDefault(item => item["Request"]?["Path"]?.GetValue<string>() == "/readings");
                if (received is null) await Task.Delay(100, timeout.Token);
            }
            var request = received["Request"]!;
            var body = request["BodyAsJson"] ?? JsonNode.Parse(request["Body"]!.GetValue<string>());
            body!["deviceId"]!.GetValue<string>().ShouldBe("trial-device");
            body["temperature"]!.GetValue<decimal>().ShouldBe(23.5m);
            body["messageId"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
            received["Response"]!["StatusCode"]!.GetValue<int>().ShouldBe(201);
            EnvironmentRunStatus observed;
            do
            {
                observed = await runner.GetAsync(status.Id, timeout.Token);
                if (observed.Outputs!.Records.Where(item => item.Message?.GetRawText().Contains("trial-device", StringComparison.Ordinal) == true)
                    .Select(item => item.InstanceId).Distinct().Count() < instanceCount ||
                    observed.Traces!.Records.Count(item => item.Name == "telemetry.send") < instanceCount)
                    await Task.Delay(100, timeout.Token);
                else break;
            } while (!timeout.IsCancellationRequested);
            observed.Outputs!.Records.ShouldContain(item => item.Message!.Value.GetRawText().Contains("trial-device", StringComparison.Ordinal));
            observed.Outputs.Records.ShouldAllBe(item => item.Error == null);
            observed.Diagnostics!.Records.ShouldContain(item => item.Message == "Workflow execution started.");
            observed.Logs!.Records.ShouldNotContain(item => item.Message == "Workflow execution started.");
            observed.Traces!.Records.ShouldContain(item => item.Source == "FluxFlow.Core"
                && item.Attributes.ContainsKey("flow.component.address")
                && item.Name == item.Attributes["flow.component.address"].GetString());
            foreach (var instanceSpans in observed.Traces.Records.GroupBy(item => item.Attributes["flow.application.instance.id"].GetString()))
            {
            var receiveSpan = instanceSpans.Single(item => item.Name == "telemetry.receive");
            receiveSpan.ParentSpanId.ShouldBeNull();
            var requestSpan = instanceSpans.Single(item => item.Name == "telemetry.request");
            requestSpan.ParentSpanId.ShouldBe(receiveSpan.SpanId);
            requestSpan.TraceId.ShouldBe(receiveSpan.TraceId);
            var sendSpan = instanceSpans.Single(item => item.Name == "telemetry.send");
            sendSpan.ParentSpanId.ShouldBe(requestSpan.SpanId);
            sendSpan.TraceId.ShouldBe(receiveSpan.TraceId);
            }
            if (authenticated) await AssertPublishDenied(publisher, "private/trial-device/temperature", rabbitMq, brokerService.ContainerId!, timeout.Token);
            (await runner.GetAsync(status.Id, timeout.Token)).Services.ShouldBe(status.Services);
            graph.ToProjectJson().ShouldBe(project);
            graph.Revision.ShouldBe(revision);
        }
        finally { await session.StopAsync(); }
        session.Status!.State.ShouldBe("Stopped");
        session.Outputs.Records.ShouldNotBeEmpty();
        session.Traces.Records.ShouldNotBeEmpty();
        session.Diagnostics.Records.Last().Message.ShouldBe("Execution stopped; run resource cleanup completed.");
        var retainedOutputs = await runner.GetAsync(status!.Id, timeout.Token);
        session.Outputs.Records.Select(item => item.Message?.GetRawText()).ShouldBe(retainedOutputs.Outputs!.Records.Select(item => item.Message?.GetRawText()));
        var retainedTraces = System.Text.Json.JsonSerializer.Serialize(session.Traces);
        System.Text.Json.JsonSerializer.Serialize(retainedOutputs.Traces).ShouldBe(retainedTraces);
        System.Text.Json.JsonSerializer.Serialize((await runner.GetAsync(status.Id, timeout.Token)).Traces).ShouldBe(retainedTraces);
        (await runner.GetAsync(status!.Id, CancellationToken.None)).State.ShouldBe("Stopped");
        foreach (var container in status!.Services.Where(item => item.ContainerId != null)) await AssertRemoved(container.ContainerId!);
        // A fresh run uses fresh resource instances with the unchanged configuration.
        EnvironmentRunStatus? restarted = null;
        try
        {
            await session.StartAsync(graph).WaitAsync(timeout.Token);
            restarted = session.Status!;
            restarted.Error.ShouldBeNull();
            restarted.State.ShouldBe("Running");
            restarted.Services.Select(item => item.Provider).ShouldBe(status.Services.Select(item => item.Provider));
            restarted.Services.Where(item => item.ContainerId != null).Select(item => item.ContainerId)
                .Intersect(status.Services.Select(item => item.ContainerId)).ShouldBeEmpty();
            session.Traces.Records.Select(item => item.SpanId)
                .Intersect(retainedOutputs.Traces!.Records.Select(item => item.SpanId)).ShouldBeEmpty();
        }
        finally { await session.StopAsync(); }
        session.Status!.State.ShouldBe("Stopped");
        (await runner.GetAsync(restarted!.Id, timeout.Token)).State.ShouldBe("Stopped");
        foreach (var container in restarted!.Services.Where(item => item.ContainerId != null)) await AssertRemoved(container.ContainerId!);
        AssertHistory(graph, beforeSetup, project, revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_after_a_real_container_starts_removes_it(bool rabbitMq)
    {
        using var cancellation = new CancellationTokenSource();
        var recording = new RecordingProvider(Broker(rabbitMq), cancellation.Cancel);
        var preparation = PreparationTests.Create(recording, new ConfiguredContainerProvider(FluxFlow.Plugins.Http.WireMockContainer.Definition));
        await Should.ThrowAsync<OperationCanceledException>(() => preparation.PrepareAsync(Configuration, BrokerProfile(rabbitMq), cancellation.Token));
        recording.Id.ShouldNotBeNull();
        await AssertRemoved(recording.Id!);
    }

    private static Task<string> ReadContainer(string id, CancellationToken cancellationToken, params string[] command) =>
        ReadDockerOutput(cancellationToken, ["exec", id, .. command]);

    private static async Task<string> ReadDockerOutput(CancellationToken cancellationToken, params string[] command)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in command) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var errors = await error;
        process.ExitCode.ShouldBe(0, errors);
        return await output + errors;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Studio_startup_failure_removes_started_services_without_changing_history(bool missingImage, bool rabbitMq)
    {
        var mqtt = new RecordingProvider(Broker(rabbitMq));
        var http = new RecordingProvider(new ConfiguredContainerProvider(FluxFlow.Plugins.Http.WireMockContainer.Definition));
        var config = JsonNode.Parse(Configuration)!;
        var profile = BrokerProfile(rabbitMq, authenticated: true);
        if (missingImage) profile.Resources["Resources:api"] = profile.Resources["Resources:api"] with { Image = "fluxflow-missing-local-test-image:1" };
        else config["Workflows"]!["telemetry"]!["request"]!["Expression"] = "(";
        var expectedError = missingImage ? profile.Resources["Resources:api"].Image!
            : Should.Throw<Exception>(() => JsonataExpressions.Compile("(")).Message;
        var graph = CreateGraph(config.ToJsonString());
        var beforeSetup = graph.ToProjectJson();
        graph.SetEnvironment(profile);
        var project = graph.ToProjectJson();
        var revision = graph.Revision;
        await using var app = await CreateCompanion(mqtt, http);
        using var client = app.GetTestClient();
        var runner = new HttpEnvironmentRunner(client);
        await using var session = new StudioRunSession(runner);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        string? failure = null;
        try
        {
            await session.StartAsync(graph).WaitAsync(timeout.Token);
            session.Status!.State.ShouldBe("Failed");
            session.Status.Error.ShouldNotBeNullOrWhiteSpace();
            session.Status.Error.ShouldContain(expectedError);
            session.Diagnostics.Records.ShouldContain(item => item.Message == "Startup failed." && item.Exception != null && item.Exception.Contains(expectedError, StringComparison.Ordinal));
            failure = session.Status.Error;
            mqtt.Id.ShouldNotBeNull();
            await AssertRemoved(mqtt.Id!);
            if (missingImage) http.Id.ShouldBeNull();
            else { http.Id.ShouldNotBeNull(); await AssertRemoved(http.Id!); }
        }
        finally { await session.StopAsync(); }
        session.Status!.State.ShouldBe("Stopped");
        session.Failure.ShouldBe(failure);
        session.Diagnostics.Records.ShouldContain(item => item.Message == "Startup failed." && item.Exception != null && item.Exception.Contains(expectedError, StringComparison.Ordinal));
        session.Diagnostics.Records.Last().Message.ShouldBe("Stop request completed.");
        session.CanRetry.ShouldBeTrue();
        (await runner.GetAsync(session.Status.Id, timeout.Token)).State.ShouldBe("Stopped");
        AssertHistory(graph, beforeSetup, project, revision);
    }

    private static IEnvironmentProvider Broker(bool rabbitMq) => rabbitMq ? new ConfiguredContainerProvider(FluxFlow.Plugins.Mqtt.RabbitMqMqttContainer.Definition) : new ConfiguredContainerProvider(FluxFlow.Plugins.Mqtt.MosquittoContainer.Definition);

    [Fact]
    public async Task RabbitMq_invalid_startup_configuration_preserves_the_error_and_removes_the_container()
    {
        var before = await RabbitMqContainers();
        var binding = BrokerProfile(true).Resources["Resources:broker"] with { Files = new()
            { ["/etc/rabbitmq/rabbitmq.conf"] = "fluxflow_invalid_setting = true\n" } };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var error = await Should.ThrowAsync<Exception>(() => new ConfiguredContainerProvider(FluxFlow.Plugins.Mqtt.RabbitMqMqttContainer.Definition).StartAsync(binding, timeout.Token));
        error.ToString().ShouldContain("fluxflow_invalid_setting");
        (await RabbitMqContainers()).Except(before).ShouldBeEmpty();
    }

    private static async Task<string[]> RabbitMqContainers()
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "ps", "--all", "--quiet", "--no-trunc", "--filter", "label=fluxflow.environment=rabbitmq-mqtt" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await error);
        return (await output).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task AssertPublishDenied(IMqttClient client, string topic, bool rabbitMq, string containerId, CancellationToken cancellationToken)
    {
        try
        {
            var denied = await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic)
                .WithPayload("1").WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(), cancellationToken);
            denied.ReasonCode.ShouldBe(MqttClientPublishReasonCode.NotAuthorized);
        }
        catch (MQTTnet.Exceptions.MqttClientDisconnectedException) when (rabbitMq)
        {
            client.IsConnected.ShouldBeFalse();
            var logs = await ReadDockerOutput(cancellationToken, "logs", containerId);
            logs.ShouldContain($"MQTT topic access refused: write access to topic '{topic.Replace('/', '.')}'", customMessage: logs);
        }
    }

    private static EnvironmentProfile BrokerProfile(bool rabbitMq, bool authenticated = false)
    {
        var profile = Profile;
        if (rabbitMq) profile.Resources["Resources:broker"] = profile.Resources["Resources:broker"] with
            { Provider = FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider.Name, Image = FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider.Image };
        if (authenticated)
        {
            var binding = profile.Resources["Resources:broker"];
            profile.Resources["Resources:broker"] = binding with { BrokerAccess = new()
            { [binding.Provider] = new() {
                Enabled = true, ClientUsername = "reader", Users =
                [
                    new() { Name = "reader", Password = "reader-test-password", Read = rabbitMq ? "^sensors\\..*$" : "sensors/#" },
                    new() { Name = "publisher", Password = "publisher-test-password", Write = rabbitMq ? "^sensors\\..*$" : "sensors/#" }
                ]
            } } };
        }
        return profile;
    }

    private static async Task<WebApplication> CreateCompanion(params IEnvironmentProvider[] providers)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLocalFlowRunner(RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Mqtt.MqttPlugin(), new FluxFlow.Plugins.Http.HttpPlugin()));
        foreach (var provider in providers) builder.Services.AddSingleton(provider);
        var app = builder.Build();
        app.MapLocalFlowRunner();
        await app.StartAsync();
        return app;
    }

    private static DesignerGraphState CreateGraph(string configuration)
    {
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Mqtt.MqttPlugin(), new FluxFlow.Plugins.Http.HttpPlugin());
        var services = new ServiceCollection().AddCatalogComponents(plugins.Descriptions.ToArray());
        foreach (var plugin in plugins.Modules) plugin.ConfigureHost(services);
        using var provider = services.BuildServiceProvider();
        var graph = new DesignerGraphState(provider.GetServices<IComponentDescription>(), provider.GetServices<IResourceEnvironmentAdapter>());
        graph.LoadJson(configuration);
        graph.ResetHistory();
        return graph;
    }

    private static void AssertHistory(DesignerGraphState graph, string beforeSetup, string project, long revision)
    {
        graph.ToProjectJson().ShouldBe(project);
        graph.Revision.ShouldBe(revision);
        graph.CanRedo.ShouldBeFalse();
        var configuration = graph.ExportConfiguration();
        graph.Undo();
        graph.ToProjectJson().ShouldBe(beforeSetup);
        graph.CanUndo.ShouldBeFalse();
        graph.Redo();
        graph.ToProjectJson().ShouldBe(project);
        graph.ExportConfiguration().ShouldBe(configuration);
    }

    internal static async Task AssertRemoved(string id)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("inspect"); start.ArgumentList.Add(id);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        (await error).ShouldContain("No such");
        process.ExitCode.ShouldNotBe(0);
    }

    private sealed class RecordingProvider(IEnvironmentProvider inner, Action? started = null) : IEnvironmentProvider
    {
        public string Name => inner.Name;
        public string ResourceType => inner.ResourceType;
        public string? Id { get; private set; }
        public void Validate(ResourceBinding binding) => inner.Validate(binding);
        public async Task<IEnvironmentService> StartAsync(ResourceBinding binding, CancellationToken cancellationToken)
        {
            var lease = await inner.StartAsync(binding, cancellationToken);
            Id = lease.ContainerId;
            started?.Invoke();
            return lease;
        }
    }
}

[CollectionDefinition("Containers", DisableParallelization = true)]
public sealed class ContainerCollection;
