using FluxFlow.Plugins.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions;
using FluxFlow.Configuration.Components;
using FluxFlow.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class CompanionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_instance_cleanup_keeps_the_original_cleanup_failure(bool failDisposal)
    {
        var failure = new IOException("Resource cleanup did not finish.");
        var host = new FailingCleanupHost(failure, failDisposal);
        var instance = new WorkflowInstanceRun(host, new FluxFlow.Application.FlowApplication(), new("test", "Test"),
            null, null, NullLogger.Instance);
        var first = await Should.ThrowAsync<AggregateException>(() => instance.DisposeAsync().AsTask());
        first.Flatten().InnerExceptions.ShouldContain(failure);
        first.Message.ShouldNotContain("deadline");
        var repeated = await Should.ThrowAsync<AggregateException>(() => instance.DisposeAsync().AsTask());
        repeated.ShouldBeSameAs(first);
        host.StopCalls.ShouldBe(1);
        host.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Host_stop_deadline_reports_its_duration_and_keeps_independent_disposal_failure()
    {
        var disposalError = new IOException("Disposal failed after the host deadline.");
        var host = new FailingCleanupHost(disposalError, failDisposal: true, waitForDeadline: true);
        var instance = new WorkflowInstanceRun(host, new FluxFlow.Application.FlowApplication(), new("test", "Test"),
            null, null, NullLogger.Instance);
        var error = await Should.ThrowAsync<AggregateException>(() => instance.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)));
        error.Message.ShouldContain("10-second host Stop deadline elapsed");
        error.Flatten().InnerExceptions.ShouldContain(e => e is OperationCanceledException);
        error.Flatten().InnerExceptions.ShouldContain(disposalError);
        host.DisposeCalls.ShouldBe(1);
    }

    private sealed class FailingCleanupHost(Exception failure, bool failDisposal, bool waitForDeadline = false) : IHost, IAsyncDisposable
    {
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public IServiceProvider Services => throw new NotSupportedException();
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            if (waitForDeadline) return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return !failDisposal ? Task.FromException(failure) : Task.CompletedTask;
        }
        public void Dispose() => throw new NotSupportedException();
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            // Like native DI, subsequent disposal can be a no-op after an exception.
            return failDisposal && DisposeCalls == 1 ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void Shutdown_distinguishes_original_execution_faults_by_identity_without_hiding_cleanup_errors()
    {
        var original = new InvalidOperationException("Pending limit reached.");
        var cleanup = new InvalidOperationException(original.Message);
        var failures = new List<Exception>();
        WorkflowInstanceRun.AddCleanupFailures(new AggregateException(original, new AggregateException(original)), [original], failures);
        failures.ShouldBeEmpty();
        WorkflowInstanceRun.AddCleanupFailures(new AggregateException(original, cleanup), [original], failures);
        failures.ShouldBe(new[] { cleanup });
        var cancellation = new OperationCanceledException();
        WorkflowInstanceRun.AddCleanupFailures(cancellation, [original], failures);
        failures.ShouldBe(new Exception[] { cleanup, cancellation });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_second_instance_stops_the_first_and_allows_rerun(bool cancel)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var observations = new RunObservations();
        var interceptor = new StartInterceptor(() =>
        {
            SpinWait.SpinUntil(() => observations.Outputs.Snapshot().Records.Count > 0, TimeSpan.FromSeconds(5)).ShouldBeTrue();
            if (cancel) cancellation.Cancel();
            else throw new InvalidOperationException("Second instance startup failed.");
        });
        using var logging = ExecutionLogTests.CreateFactory();
        logging.AddProvider(interceptor);
        using var traces = new ExecutionTraceCollector();
        var disposed = new List<string>();
        var provider = new PreparationTests.FakeProvider("http", "http.client", new Uri("http://localhost:1"), disposed);
        using var plugins = RunTestPlugins.WithBasicComponents(new HttpPlugin());
        var execution = new WorkflowExecution(PreparationTests.Create(provider), logging, traces, plugins: plugins);
        var request = new EnvironmentRunRequest("""
            {"Resources":{"api":{"Type":"http.client"}},"Workflows":{"main":{"tick":{"Type":"scheduler","Interval":"00:00:00.025","Output":"map.Input"},
              "map":{"Type":"mapper","Engine":"jsonata","Expression":"$"}}}}
            """, new() { Resources = new() { ["Resources:api"] = new() { Provider = "http" } } })
            { Instances = [new("first", "First"), new("second", "Second")] };
        await Should.ThrowAsync<Exception>(() => execution.StartAsync(request, cancellation.Token, observations));
        var retained = System.Text.Json.JsonSerializer.Serialize(observations.Outputs.Snapshot());
        await Task.Delay(150);
        System.Text.Json.JsonSerializer.Serialize(observations.Outputs.Snapshot()).ShouldBe(retained);
        observations.Outputs.Snapshot().Records.ShouldAllBe(output => output.InstanceId == "first");
        observations.Outputs.Snapshot().Records.ShouldNotBeEmpty();
        disposed.ShouldBe(new[] { "http" });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var next = new RunObservations();
        await using (var run = await execution.StartAsync(request, deadline.Token, next))
        {
            while (next.Outputs.Snapshot().Records.Where(output => output.Port == "main.map.Output").Select(output => output.InstanceId).Distinct().Count() < 2)
                await Task.Delay(25, deadline.Token);
        }
        System.Text.Json.JsonSerializer.Serialize(observations.Outputs.Snapshot()).ShouldBe(retained);
        provider.Starts.ShouldBe(2);
        disposed.ShouldBe(new[] { "http", "http" });
        next.Traces.Snapshot().Records.SelectMany(span => span.Attributes)
            .Where(attribute => attribute.Key == "flow.application.instance.id").Select(attribute => attribute.Value.GetString()).Distinct().Order()
            .ShouldBe(new[] { "first", "second" });
    }

    private sealed class StartInterceptor(Action interrupt) : ILoggerProvider
    {
        private int _starts;
        private void Interrupt() => interrupt();
        public ILogger CreateLogger(string categoryName) => new CallbackLogger(this);
        public void Dispose() { }
        private sealed class CallbackLogger(StartInterceptor owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (formatter(state, exception) == "Starting workflow execution." && Interlocked.Increment(ref owner._starts) == 2) owner.Interrupt();
            }
        }
    }

    [Fact]
    public void Companion_registers_the_installed_palette()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Workflows:main:record:Type"] = "logger" }).Build();
        var services = new ServiceCollection();
        using var plugins = RunTestPlugins.WithBasicComponents(new HttpPlugin(), new FluxFlow.Plugins.Ros2.RosPlugin());
        WorkflowExecution.Register(services, configuration, plugins);
        services.AddCatalogComponents(plugins.Descriptions.ToArray());
        using var provider = services.BuildServiceProvider();
        var missing = provider.GetServices<IComponentDescription>()
            .Select(description => description.Palette.ComponentType)
            .Where(type => provider.GetKeyedService<ComponentRegistration>(type) is null)
            .OrderBy(type => type).ToArray();
        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Serial_registration_validates_workflow_resources_without_opening_a_device()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Workflows:main:Resources:port:Type"] = "serial.port",
                ["Workflows:main:Resources:port:PortName"] = "nonexistent-test-port",
                ["Workflows:main:text:Type"] = "serial.text",
                ["Workflows:main:text:Connection"] = "port"
            }).Build();
        var services = new ServiceCollection();
        using var plugins = new FluxFlow.Execution.Plugins.InstalledPlugins(new FluxFlow.Plugins.Serial.SerialPlugin());
        WorkflowExecution.Register(services, configuration, plugins);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredKeyedService<ComponentRegistration>("serial.text").ShouldNotBeNull();
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Scheduler_delivers_repeated_logs_through_the_companion_and_stops_without_changing_configuration(bool processingPipeline, int instanceCount)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Routing.RoutingPlugin());
        builder.Services.AddLocalFlowRunner(plugins);
        await using var app = builder.Build();
        app.MapLocalFlowRunner();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        var request = new EnvironmentRunRequest(processingPipeline ? """
            {"Workflows":{"main":{
              "tick":{"Type":"scheduler","Interval":"00:00:00.050","Output":"values.Input"},
              "values":{"Type":"source","Values":"[1,2,3]","Output":"generate.Input"},
              "generate":{"Type":"dataGenerator","Expression":"1","Output":"changed.Input"},
              "changed":{"Type":"changeFilter","Engine":"jsonata","KeySelector":"$messageId","Output":"unique.Input"},
              "unique":{"Type":"deduplicate","Engine":"jsonata","KeySelector":"$messageId","Retention":"00:00:10","Output":"batch.Input"},
              "batch":{"Type":"batch","BatchSize":1,"MaxWait":"00:00:01","Output":"reduce.Input"},
              "reduce":{"Type":"reducer","Engine":"jsonata","InitialState":"0","Reduce":"$state + 1","AreEqual":"$previous = $current","Output":"gate.Input"},
              "gate":{"Type":"gate","InitiallyOpen":true,"Output":"route.Input"},
              "route":{"Type":"router","Engine":"jsonata","Routes":{"Accepted":"true"},"Accepted":"exchange.Input"},
              "exchange":{"Type":"requestReply","MaxInFlight":2,"Request":"retry.Input","ACK":"retry.Output","Output":"format.Input"},
              "retry":{"Type":"retry","Request":"worker.Input","Reply":"worker.Output"},
              "worker":{"Type":"mapper","Engine":"jsonata","Expression":"$"},
              "format":{"Type":"mapper","Engine":"jsonata","Expression":"{\"MessageTemplate\":\"Scheduler tick\",\"LogLevel\":\"Information\"}","Output":"record.Input"},
              "record":{"Type":"logger"}
            }}}
            """ : """
            {"Workflows":{"main":{
              "tick":{"Type":"scheduler","Interval":"00:00:00.050","Output":"format.Input"},
              "format":{"Type":"mapper","Engine":"jsonata","Expression":"{\"MessageTemplate\":\"Scheduler tick\",\"LogLevel\":\"Information\"}","Output":"record.Input"},
              "record":{"Type":"logger"}
            }}}
            """, new());
        request = request with { Instances = Enumerable.Range(1, instanceCount).Select(i => new ApplicationInstance($"instance-{i}", $"Application {i}")).ToArray() };
        var original = System.Text.Json.JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        var id = Guid.NewGuid();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var check = await runner.CheckAsync(request, deadline.Token);
            check.Success.ShouldBeTrue();
            check.Message.ShouldContain("Docker is not required");
            var status = await runner.StartAsync(id, request, deadline.Token);
            status.State.ShouldBe("Running", status.Error);
            do
            {
                await Task.Delay(25, deadline.Token);
                status = await runner.GetAsync(id, deadline.Token);
                status.State.ShouldBe("Running", status.Error);
            } while (status.Logs!.Records.Count(record => record.Category == "main.record" && record.Message == "Scheduler tick") < 2
                || request.Instances.Any(instance => status.Outputs!.Records.Count(record => record.Port == "main.tick.Output" && record.InstanceId == instance.Id) < 2)
                || request.Instances.Any(instance => !status.Logs!.Records.Any(record => record.Category == "main.record" &&
                    record.Telemetry!.Attributes.Any(attribute => attribute.Name == "flow.application.instance.id" && attribute.Value.GetString() == instance.Id))));

            status.Services.ShouldBeEmpty();
            status.Logs.Records.Where(record => record.Category == "main.record").ShouldAllBe(record =>
                record.Message == "Scheduler tick" && record.Level == "Information" && record.Exception == null);
            status.Diagnostics!.Records.ShouldNotContain(record => record.Message == "Scheduler tick");
            await runner.StopAsync(id, deadline.Token);
            var stopped = await runner.GetAsync(id, deadline.Token);
            stopped.State.ShouldBe("Stopped");
            stopped.Services.ShouldBeEmpty();
            System.Text.Json.JsonSerializer.Serialize(stopped.Logs!.Records.Take(status.Logs.Records.Count))
                .ShouldBe(System.Text.Json.JsonSerializer.Serialize(status.Logs.Records));
            stopped.Outputs!.Records.ShouldContain(record => record.Port == "main.tick.Output");
            if (processingPipeline)
            {
                var completed = stopped.Outputs.Records.Where(record => record.Port == "main.exchange.Output").ToArray();
                completed.ShouldNotBeEmpty();
                foreach (var record in completed)
                {
                    record.Error.ShouldBeNull();
                    var message = record.Message!.Value;
                    message.GetProperty("Error").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
                    var result = message.GetProperty("Value");
                    result.GetProperty("Outcome").GetString().ShouldBe("ACK");
                    result.GetProperty("RequestId").GetString().ShouldNotBeNullOrWhiteSpace();
                    result.GetProperty("Request").GetRawText().ShouldBe(result.GetProperty("Reply").GetRawText());
                }
            }
            var retained = System.Text.Json.JsonSerializer.Serialize(stopped, EnvironmentJsonContext.Default.EnvironmentRunStatus);
            var logs = System.Text.Json.JsonSerializer.Serialize(stopped.Logs, EnvironmentJsonContext.Default.ExecutionLogSnapshot);
            await Task.Delay(150, deadline.Token);
            var later = await runner.GetAsync(id, deadline.Token);
            System.Text.Json.JsonSerializer.Serialize(later, EnvironmentJsonContext.Default.EnvironmentRunStatus).ShouldBe(retained);
            System.Text.Json.JsonSerializer.Serialize(later.Logs, EnvironmentJsonContext.Default.ExecutionLogSnapshot).ShouldBe(logs);
            System.Text.Json.JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest).ShouldBe(original);
        }
        finally
        {
            await runner.StopAsync(id, CancellationToken.None);
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData("logs", false)]
    [InlineData("diagnostics", false)]
    [InlineData("traces", false)]
    [InlineData("logs", true)]
    [InlineData("diagnostics", true)]
    [InlineData("traces", true)]
    public async Task Telemetry_reads_cancel_without_accepting_a_late_response(string signal, bool ignoreCancellation)
    {
        var id = Guid.NewGuid();
        using var handler = new TelemetryHandler(id, signal, ignoreCancellation);
        using var client = new HttpClient(handler) { BaseAddress = new("http://localhost/") };
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        using var cancellation = new CancellationTokenSource();
        var pending = runner.GetAsync(id, cancellation.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        handler.Release.TrySetResult();
        await Should.ThrowAsync<OperationCanceledException>(() => pending);
        var next = await runner.GetAsync(id, CancellationToken.None);
        next.Id.ShouldBe(id);
        next.Logs!.Records.ShouldBeEmpty();
        next.Diagnostics!.Records.ShouldBeEmpty();
        next.Traces!.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Telemetry_reads_reject_another_runs_status_before_fetching_records()
    {
        using var handler = new TelemetryHandler(Guid.NewGuid(), "logs", false);
        using var client = new HttpClient(handler) { BaseAddress = new("http://localhost/") };
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        await Should.ThrowAsync<InvalidOperationException>(() => runner.GetAsync(Guid.NewGuid(), CancellationToken.None));
        handler.Entered.Task.IsCompleted.ShouldBeFalse();
    }

    private sealed class TelemetryHandler(Guid id, string signal, bool ignoreCancellation) : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.GetValues("X-FluxFlow-Run").Single().ShouldBe("1");
            if (!request.RequestUri!.AbsolutePath.Contains("/telemetry/", StringComparison.Ordinal))
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new EnvironmentRunStatus(id, "Running", []), EnvironmentJsonContext.Default.EnvironmentRunStatus)
                };
            if (request.RequestUri.AbsolutePath.EndsWith($"/{signal}", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ignoreCancellation ? CancellationToken.None : cancellationToken);
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentType = new("application/x-protobuf");
            response.Headers.Add("X-FluxFlow-Dropped", "0");
            return response;
        }
    }

    [Theory]
    [InlineData("\"node\":{\"Type\":\"http.trigger\",\"Route\":\"\"}", "{}", "main.node", "Route")]
    [InlineData("\"node\":{\"Type\":\"ros.publisher\",\"Node\":\"ros\",\"Topic\":\"\"}", "{\"ros\":{\"Type\":\"ros.node\",\"Name\":\"test\"}}", "main.node", "Topic")]
    [InlineData("\"node\":{\"Type\":\"ros.subscriber\"}", "{}", "main.node", "Node")]
    [InlineData("\"node\":{\"Type\":\"rateLimit\",\"Limiter\":\"limits\"}", "{}", "main.node", "main.Resources.limits")]
    [InlineData("\"node\":{\"Type\":\"unregistered\"}", "{}", "main.node", "Not available")]
    [InlineData("\"node\":{\"Type\":\"serial.text\",\"Connection\":\"port\"}", "{}", "main.node", "workflow-local serial port")]
    [InlineData("\"node\":{\"Type\":\"serial.text\",\"Connection\":\"port\"}", "{\"port\":{\"Type\":\"serial.port\",\"PortName\":\"nonexistent-test-port\",\"BaudRate\":0}}", "main.Resources.port", "BaudRate")]
    [InlineData("\"node\":{\"Type\":\"serial.text\",\"Connection\":\"port\"},\"second\":{\"Type\":\"serial.text\",\"Connection\":\"port\"}", "{\"port\":{\"Type\":\"serial.port\",\"PortName\":\"nonexistent-test-port\"}}", "main.second", "already has a SerialText owner")]
    [InlineData("\"node\":{\"Type\":\"http.trigger\",\"Route\":\"/test\"},\"second\":{\"Type\":\"unregistered\"}", "{}", "main.second", "Not available")]
    public async Task Environment_check_names_runtime_limitations_without_probing_Docker_or_starting_resources(
        string components, string resources, string affectedComponent, string reason)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLocalFlowRunner(new FluxFlow.Execution.Plugins.InstalledPlugins(new FluxFlow.Plugins.Serial.SerialPlugin(), new FluxFlow.Plugins.FlowControl.FlowControlPlugin(), new HttpPlugin(), new FluxFlow.Plugins.Ros2.RosPlugin()));
        var provider = new WaitingProvider();
        var runtime = new RuntimeCheck();
        builder.Services.AddSingleton<IEnvironmentProvider>(provider);
        builder.Services.AddSingleton<IContainerRuntimeCheck>(runtime);
        await using var app = builder.Build();
        app.MapLocalFlowRunner();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        var workflow = JsonNode.Parse("{" + components + "}")!;
        workflow["Resources"] = JsonNode.Parse(resources);
        var configuration = new JsonObject
        {
            ["Resources"] = JsonNode.Parse("""{"api":{"Type":"http.client","BaseAddress":"https://original/"}}"""),
            ["Workflows"] = new JsonObject { ["main"] = workflow }
        };
        var request = new EnvironmentRunRequest(configuration.ToJsonString(),
            new() { Resources = new() { ["Resources:api"] = new() { Provider = "waiting" } } });
        var original = System.Text.Json.JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await runner.CheckAsync(request, deadline.Token);
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain(affectedComponent);
        result.Message.ShouldContain(reason);
        runtime.Calls.ShouldBe(0);
        provider.Started.Task.IsCompleted.ShouldBeFalse();
        System.Text.Json.JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest).ShouldBe(original);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => runner.CheckAsync(request, cancelled.Token));
        runtime.Calls.ShouldBe(0);
        await app.StopAsync();
    }

    [Theory]
    [InlineData("existing", false, true, 0)]
    [InlineData("waiting", false, true, 1)]
    [InlineData("waiting", true, false, 1)]
    [InlineData("missing", false, false, 0)]
    public async Task Environment_check_is_read_only_and_only_contacts_Docker_for_valid_container_setup(string providerName, bool dockerUnavailable, bool success, int probes)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        using var plugins = new FluxFlow.Execution.Plugins.InstalledPlugins(new HttpPlugin());
        builder.Services.AddLocalFlowRunner(plugins);
        var provider = new WaitingProvider();
        var runtime = new RuntimeCheck { Unavailable = dockerUnavailable };
        builder.Services.AddSingleton<IEnvironmentProvider>(provider);
        builder.Services.AddSingleton<IContainerRuntimeCheck>(runtime);
        await using var app = builder.Build();
        app.MapLocalFlowRunner();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var body = new EnvironmentRunRequest("""{"Resources":{"api":{"Type":"http.client","BaseAddress":"https://original/"}},"Workflows":{"main":{}}}""",
            new() { Resources = new() { ["Resources:api"] = new() { Provider = providerName } } });
        var original = System.Text.Json.JsonSerializer.Serialize(body, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        using (var denied = await client.PostAsJsonAsync("/api/runs/check", body, EnvironmentJsonContext.Default.EnvironmentRunRequest))
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Add("X-FluxFlow-Run", "1");
        client.DefaultRequestHeaders.Add("Origin", "https://foreign.example");
        using (var denied = await client.PostAsJsonAsync("/api/runs/check", body, EnvironmentJsonContext.Default.EnvironmentRunRequest))
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        runtime.Calls.ShouldBe(0);
        client.DefaultRequestHeaders.Remove("Origin");
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        var result = await runner.CheckAsync(body, CancellationToken.None);
        result.Success.ShouldBe(success);
        if (providerName == "existing") result.Message.ShouldContain("Docker is not required");
        if (dockerUnavailable) result.Message.ShouldContain("Docker is unavailable");
        runtime.Calls.ShouldBe(probes);
        provider.Started.Task.IsCompleted.ShouldBeFalse();
        System.Text.Json.JsonSerializer.Serialize(body, EnvironmentJsonContext.Default.EnvironmentRunRequest).ShouldBe(original);
        (await runner.GetAsync(Guid.NewGuid(), CancellationToken.None)).State.ShouldBe("Stopped");
    }

    [Fact]
    public async Task Environment_check_propagates_cancellation_and_rejects_invalid_broker_setup_before_Docker()
    {
        var runtime = new RuntimeCheck();
        var check = new EnvironmentCheck(PreparationTests.Create(new FluxFlow.Environments.Testcontainers.ConfiguredContainerProvider(FluxFlow.Plugins.Mqtt.MosquittoContainer.Definition)), [runtime]);
        var binding = new ResourceBinding { Provider = "mosquitto", BrokerAccess = new() { ["mosquitto"] = new()
            { Enabled = true, ClientUsername = "missing", Users = [new() { Name = "reader", Password = "test-secret" }] } } };
        var request = new EnvironmentRunRequest("""{"Resources":{"broker":{"Type":"mqtt.client"}},"Workflows":{"main":{}}}""",
            new() { Resources = new() { ["Resources:broker"] = binding } });
        var invalid = await check.CheckAsync(request, CancellationToken.None);
        invalid.Success.ShouldBeFalse();
        invalid.Message.ShouldNotContain("test-secret");
        runtime.Calls.ShouldBe(0);

        request.Environment.Resources["Resources:broker"] = new() { Provider = "mosquitto", Files = new() { ["relative.conf"] = "invalid" } };
        (await check.CheckAsync(request, CancellationToken.None)).Success.ShouldBeFalse();
        runtime.Calls.ShouldBe(0);
        request.Environment.Resources["Resources:broker"] = new() { Provider = "mosquitto", Image = "changed:1",
            Profile = new("locked", "Locked broker", true, new() { Provider = "mosquitto", Image = "original:1" }) };
        (await check.CheckAsync(request, CancellationToken.None)).Message.ShouldContain("locked setup");
        runtime.Calls.ShouldBe(0);

        request.Environment.Resources["Resources:broker"] = new() { Provider = "mosquitto" };
        runtime.Wait = true;
        using var cancellation = new CancellationTokenSource();
        var pending = check.CheckAsync(request, cancellation.Token);
        await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try { await Should.ThrowAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5))); }
        finally { runtime.Release.TrySetResult(); }
        runtime.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Environment_check_times_out_even_when_the_Docker_probe_ignores_cancellation()
    {
        var provider = new WaitingProvider();
        var runtime = new RuntimeCheck { Wait = true };
        var check = new EnvironmentCheck(PreparationTests.Create(provider), [runtime]);
        var request = new EnvironmentRunRequest("""{"Resources":{"api":{"Type":"http.client"}},"Workflows":{"main":{}}}""",
            new() { Resources = new() { ["Resources:api"] = new() { Provider = provider.Name } } });
        try
        {
            var result = await check.CheckAsync(request, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
            result.Success.ShouldBeFalse();
            result.Message.ShouldContain("Docker did not respond within 10 seconds");
            runtime.Calls.ShouldBe(1);
            provider.Started.Task.IsCompleted.ShouldBeFalse();
        }
        finally { runtime.Release.TrySetResult(); }
    }

    private sealed class RuntimeCheck : IContainerRuntimeCheck
    {
        public bool Unavailable { get; init; }
        public bool Wait { get; set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task CheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            if (Wait) await Release.Task;
            if (Unavailable) throw new IOException("Daemon unavailable.");
        }
    }

    [Fact]
    public async Task Registry_operations_use_the_companion_origin_checks_and_do_not_return_credentials()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLocalFlowRunner();
        var access = new RegistryAccess();
        builder.Services.AddSingleton<IRegistryAccess>(access);
        await using var app = builder.Build();
        app.MapLocalFlowRunner();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var request = new RegistrySignIn(new() { Address = "registry.example" }, "user", "example-token");
        using (var denied = await client.PostAsJsonAsync("/api/runs/registries/sign-in", request, EnvironmentJsonContext.Default.RegistrySignIn))
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Add("X-FluxFlow-Run", "1");
        client.DefaultRequestHeaders.Add("Origin", "https://foreign.example");
        using (var denied = await client.PostAsJsonAsync("/api/runs/registries/sign-in", request, EnvironmentJsonContext.Default.RegistrySignIn))
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        access.Request.ShouldBeNull();
        client.DefaultRequestHeaders.Remove("Origin");
        using var response = await client.PostAsJsonAsync("/api/runs/registries/sign-in", request, EnvironmentJsonContext.Default.RegistrySignIn);
        response.EnsureSuccessStatusCode();
        access.Request.ShouldBe(request);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("example-token");
        await app.StopAsync();
    }

    private sealed class RegistryAccess : IRegistryAccess
    {
        public RegistrySignIn? Request { get; private set; }
        public Task<RegistryAccessResult> TestAsync(ImageRegistry registry, CancellationToken cancellationToken) => Task.FromResult(new RegistryAccessResult(true, "Reachable"));
        public Task<RegistryAccessResult> SignInAsync(RegistrySignIn request, CancellationToken cancellationToken) { Request = request; return Task.FromResult(new RegistryAccessResult(true, "Signed in")); }
        public Task<RegistryAccessResult> SignOutAsync(ImageRegistry registry, CancellationToken cancellationToken) => Task.FromResult(new RegistryAccessResult(true, "Signed out"));
    }

    [Fact]
    public async Task Companion_shutdown_cancels_preparation_and_rejects_new_runs()
    {
        var provider = new WaitingProvider();
        using var logging = ExecutionLogTests.CreateFactory();
        using var plugins = new FluxFlow.Execution.Plugins.InstalledPlugins(new HttpPlugin());
        var preparation = PreparationTests.Create(provider);
        var execution = new WorkflowExecution(preparation, logging, plugins: plugins);
        using var runner = new LocalEnvironmentRunner(execution, logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var request = new EnvironmentRunRequest("""{"Resources":{"api":{"Type":"http.client"}},"Workflows":{"main":{}}}""",
            new() { Resources = new() { ["Resources:api"] = new() { Provider = provider.Name } } });
        var id = Guid.NewGuid();
        var start = runner.StartAsync(id, request, CancellationToken.None);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await runner.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await start;
            provider.Cancelled.ShouldBeTrue();
            var stopped = await runner.GetAsync(id, CancellationToken.None);
            stopped.State.ShouldBe("Stopped");
            stopped.Diagnostics!.Records.ShouldContain(record => record.Message == "Preparing execution environment.");
            stopped.Diagnostics.Records.ShouldContain(record => record.Message == "Startup cancelled.");
            stopped.Diagnostics.Records.Last().Message.ShouldBe("Stop request completed.");
            stopped.Logs!.Records.ShouldBeEmpty();
            System.Text.Json.JsonSerializer.Serialize((await runner.GetAsync(id, CancellationToken.None)).Diagnostics)
                .ShouldBe(System.Text.Json.JsonSerializer.Serialize(stopped.Diagnostics));
            await Should.ThrowAsync<InvalidOperationException>(() => runner.StartAsync(Guid.NewGuid(), request, CancellationToken.None));
        }
        finally { await runner.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Local_API_rejects_foreign_origins_and_uses_existing_hosting_with_idempotent_cleanup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLocalFlowRunner();
        await using var app = builder.Build();
        app.MapLocalFlowRunner();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var id = Guid.NewGuid();
        var body = new EnvironmentRunRequest("""{"Workflows":{"main":{}}}""", new());
        using (var denied = await client.PostAsJsonAsync($"/api/runs/{id}", body, EnvironmentJsonContext.Default.EnvironmentRunRequest))
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Add("X-FluxFlow-Run", "1");
        client.DefaultRequestHeaders.Add("Origin", "https://foreign.example");
        using (var denied = await client.PostAsJsonAsync($"/api/runs/{id}", body, EnvironmentJsonContext.Default.EnvironmentRunRequest))
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        foreach (var signal in new[] { "logs", "diagnostics", "traces" })
        {
            using var denied = await client.GetAsync($"/api/runs/{id}/telemetry/{signal}");
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        client.DefaultRequestHeaders.Remove("Origin");
        using var response = await client.PostAsJsonAsync($"/api/runs/{id}", body, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync(EnvironmentJsonContext.Default.EnvironmentRunStatus);
        status!.State.ShouldBe("Running");
        status.Diagnostics.ShouldBeNull();
        status.Logs.ShouldBeNull();
        status.Traces.ShouldBeNull();
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        status = await runner.GetAsync(id, CancellationToken.None);
        status.Diagnostics!.Records.ShouldContain(record => record.Message == "Workflow execution started.");
        status.Logs!.Records.ShouldNotContain(record => record.Message == "Workflow execution started.");
        using (var telemetry = await client.GetAsync($"/api/runs/{id}/telemetry/diagnostics"))
        {
            telemetry.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-protobuf");
            telemetry.Headers.CacheControl!.NoStore.ShouldBeTrue();
            var payload = OpenTelemetry.Proto.Collector.Logs.V1.ExportLogsServiceRequest.Parser.ParseFrom(await telemetry.Content.ReadAsByteArrayAsync());
            payload.ResourceLogs.SelectMany(resource => resource.ScopeLogs).SelectMany(scope => scope.LogRecords)
                .ShouldContain(record => record.Body.StringValue == "Workflow execution started.");
        }
        (await client.DeleteAsync($"/api/runs/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/runs/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync($"/api/runs/{id}", EnvironmentJsonContext.Default.EnvironmentRunStatus))!.State.ShouldBe("Stopped");
        var stopped = await runner.GetAsync(id, CancellationToken.None);
        stopped.Diagnostics!.Records.ShouldContain(record => record.Message == "Execution stopped; run resource cleanup completed.");
        (await runner.GetAsync(Guid.NewGuid(), CancellationToken.None)).Diagnostics!.Records.ShouldBeEmpty();
        await app.StopAsync();
    }

    private sealed class WaitingProvider : IEnvironmentProvider
    {
        public string Name => "waiting";
        public string ResourceType => "http.client";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        public void Validate(ResourceBinding binding) { }
        public async Task<IEnvironmentService> StartAsync(ResourceBinding binding, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            throw new TimeoutException("Preparation was not cancelled.");
        }
    }
}
