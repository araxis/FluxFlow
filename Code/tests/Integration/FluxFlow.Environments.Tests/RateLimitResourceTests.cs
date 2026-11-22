using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.RateLimiting;
using FluxFlow.Core.Messaging;
using FluxFlow.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class RateLimitResourceTests
{
    [Theory]
    [InlineData("budget", true)]
    [InlineData("Resources.budget", true)]
    [InlineData("main.Resources.budget", true)]
    [InlineData("other.Resources.budget", false)]
    public async Task Companion_check_only_accepts_application_or_own_workflow_limiters(string reference, bool accepted)
    {
        var request = new EnvironmentRunRequest("""
            {"Resources":{"budget":{"Type":"rateLimiter.tokenBucket"}},
             "Workflows":{"main":{"Resources":{"budget":{"Type":"rateLimiter.tokenBucket"}},
               "limit":{"Type":"rateLimit","Limiter":"REFERENCE"}},
               "other":{"Resources":{"budget":{"Type":"rateLimiter.tokenBucket"}}}}}
            """.Replace("REFERENCE", reference), new());
        var original = JsonSerializer.Serialize(request);
        var preparation = new EnvironmentPreparation([new FluxFlow.Plugins.FlowControl.TokenBucketEnvironmentAdapter()], []);
        using var logging = ExecutionLogTests.CreateFactory();
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var result = await runner.CheckAsync(request, CancellationToken.None);
        result.Success.ShouldBe(accepted, result.Message);
        if (!accepted) result.Message.ShouldContain("application resource");
        JsonSerializer.Serialize(request).ShouldBe(original);
    }

    [Theory]
    [InlineData("TokenLimit", "0")]
    [InlineData("TokensPerPeriod", "0")]
    [InlineData("QueueLimit", "-1")]
    [InlineData("ReplenishmentPeriod", "\"00:00:00\"")]
    [InlineData("ReplenishmentPeriod", "\"00:00:00.0000001\"")]
    [InlineData("ReplenishmentPeriod", "\"50.00:00:00\"")]
    public void Invalid_resource_settings_fail_during_registration(string property, string value)
    {
        using var config = Configuration("""{"Resources":{"budget":{"Type":"rateLimiter.tokenBucket","PROPERTY":VALUE}},"Workflows":{"main":{}}}""".Replace("PROPERTY", property).Replace("VALUE", value));
        var error = Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddTokenBucketResource().AddFluxFlowApplication(config));
        error.InnerException.ShouldBeOfType<ArgumentOutOfRangeException>().ParamName.ShouldBe(property);
    }

    [Fact]
    public async Task Resources_share_their_scope_bound_queues_and_are_disposed_by_their_owner()
    {
        const string resource = """{"Type":"rateLimiter.tokenBucket","TokenLimit":1,"TokensPerPeriod":1,"ReplenishmentPeriod":"01:00:00","QueueLimit":1}""";
        using var config = Configuration("""{"Resources":{"shared":RESOURCE},"Workflows":{"first":{"Resources":{"local":RESOURCE}},"second":{"Resources":{"local":RESOURCE}}}}""".Replace("RESOURCE", resource));
        await using var provider = new ServiceCollection().AddLogging().AddTokenBucketResource().AddFluxFlowApplication(config).BuildServiceProvider();
        var first = provider.GetWorkflowServices("first");
        var second = provider.GetWorkflowServices("second");
        var shared = first.GetRequiredFlowResource<RateLimiter>("shared", "first");
        second.GetRequiredFlowResource<RateLimiter>("shared", "second").ShouldBeSameAs(shared);
        var local = first.GetRequiredFlowResource<RateLimiter>("local", "first");
        second.GetRequiredFlowResource<RateLimiter>("local", "second").ShouldNotBeSameAs(local);
        Should.Throw<InvalidOperationException>(() => first.GetRequiredFlowResource<RateLimiter>("second.Resources.local", "first"))
            .Message.ShouldContain("Use an application resource for sharing");
        using var initial = shared.AttemptAcquire();
        initial.IsAcquired.ShouldBeTrue();
        using var cancellation = new CancellationTokenSource();
        var queued = shared.AcquireAsync(1, cancellation.Token).AsTask();
        shared.GetStatistics()!.CurrentQueuedCount.ShouldBe(1);
        using var rejected = await shared.AcquireAsync();
        rejected.IsAcquired.ShouldBeFalse();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => queued);
        shared.GetStatistics()!.CurrentQueuedCount.ShouldBe(0);
        using var independent = local.AttemptAcquire();
        independent.IsAcquired.ShouldBeTrue();
        var pending = local.AcquireAsync().AsTask();
        await provider.DisposeAsync();
        using var abandoned = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        abandoned.IsAcquired.ShouldBeFalse();
        Should.Throw<ObjectDisposedException>(() => shared.AttemptAcquire());
        Should.Throw<ObjectDisposedException>(() => local.AttemptAcquire());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Companion_drains_queued_requests_and_reply_timeouts_and_reruns_with_a_fresh_budget(bool timeout)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var replyLink = timeout ? "" : ",\"Request\":\"limit.Reply\"";
        var request = new EnvironmentRunRequest("""
            {"Resources":{"budget":{"Type":"rateLimiter.tokenBucket","TokenLimit":1,"TokensPerPeriod":1,"ReplenishmentPeriod":"00:00:00.400","QueueLimit":2}},
             "Workflows":{"main":{
               "tick":{"Type":"scheduler","Interval":"00:00:00.020","Output":"once.Input"},
               "once":{"Type":"source","Values":"[{\"sequence\":1},{\"sequence\":2},{\"sequence\":3}]","Output":"limit.Input"},
               "limit":{"Type":"rateLimit","Limiter":"budget","ResponseTimeout":"00:00:00.150"REPLY_LINK}
             }}}
            """.Replace("REPLY_LINK", replyLink), new());
        var original = JsonSerializer.Serialize(request);
        var preparation = new EnvironmentPreparation([new FluxFlow.Plugins.FlowControl.TokenBucketEnvironmentAdapter()], []);
        using var logging = ExecutionLogTests.CreateFactory();
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var check = await runner.CheckAsync(request, token);
        check.Success.ShouldBeTrue(check.Message);
        Guid? previous = null;
        string? retained = null;
        var correlations = new HashSet<string>();
        foreach (var id in new[] { Guid.NewGuid(), Guid.NewGuid() })
        {
            try
            {
                (await runner.StartAsync(id, request, token)).State.ShouldBe("Running");
                while (Messages(await runner.GetAsync(id, token), "once.Output").Length < 3) await Task.Delay(10, token);
                await runner.StopAsync(id, token).WaitAsync(token);
                var stopped = await runner.GetAsync(id, token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var dispatched = Messages(stopped, "limit.Request");
                dispatched.Length.ShouldBe(3);
                dispatched.Select(message => message.GetProperty("Value").GetProperty("sequence").GetInt32()).Order().ShouldBe(new[] { 1, 2, 3 });
                dispatched.Select(message => message.GetProperty("CorrelationId").GetString()).Distinct().Count().ShouldBe(3);
                foreach (var message in dispatched) correlations.Add(message.GetProperty("CorrelationId").GetString()!).ShouldBeTrue();
                var output = Messages(stopped, "limit.Output");
                output.Length.ShouldBe(3);
                output.ShouldAllBe(message => (message.GetProperty("Error").ValueKind == JsonValueKind.Object) == timeout);
                if (!timeout) output.Select(message => message.GetProperty("Value").GetProperty("sequence").GetInt32()).Order().ShouldBe(new[] { 1, 2, 3 });
                if (previous is { } old) JsonSerializer.Serialize((await runner.GetAsync(old, token)).Outputs).ShouldBe(retained);
                previous = id;
                retained = JsonSerializer.Serialize(stopped.Outputs);
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
        }
        JsonSerializer.Serialize(request).ShouldBe(original);
    }

    [Fact]
    public async Task Two_workflows_share_one_queue_slot_and_drain_the_waiting_request_on_Stop()
    {
        using var config = Configuration("""
            {"Resources":{"budget":{"Type":"rateLimiter.tokenBucket","TokenLimit":1,"TokensPerPeriod":1,
                                    "ReplenishmentPeriod":"00:00:02","QueueLimit":1}},
             "Workflows":{"first":{"limit":{"Type":"rateLimit","Limiter":"budget","ResponseTimeout":"00:00:05","Request":"limit.Reply"}},
                          "second":{"limit":{"Type":"rateLimit","Limiter":"budget","ResponseTimeout":"00:00:05"}}}}
            """);
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var attemptIds = new HashSet<CorrelationId>();
        for (var run = 0; run < 2; run++)
        {
            var services = new ServiceCollection();
            WorkflowExecution.Register(services, config, plugins);
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            await app.StartAsync(token);

            ITargetBlock<FlowMessage> Target(string workflow, string port) => provider.GetWorkflowServices(workflow)
                .GetRequiredKeyedService<ITargetBlock<FlowMessage>>($"{workflow}.limit.{port}");
            BufferBlock<FlowMessage> Observe(string workflow, string port)
            {
                var sink = new BufferBlock<FlowMessage>();
                provider.GetWorkflowServices(workflow).GetRequiredKeyedService<ISourceBlock<FlowMessage>>($"{workflow}.limit.{port}")
                    .LinkTo(sink, new() { PropagateCompletion = true });
                return sink;
            }

            var firstRequests = Observe("first", "Request");
            var firstOutputs = Observe("first", "Output");
            var secondRequests = Observe("second", "Request");
            var secondOutputs = Observe("second", "Output");
            var firstInput = Target("first", "Input");
            var secondReply = Target("second", "Reply");
            var budget = provider.GetWorkflowServices("first").GetRequiredFlowResource<RateLimiter>("budget", "first");
            provider.GetWorkflowServices("second").GetRequiredFlowResource<RateLimiter>("budget", "second").ShouldBeSameAs(budget);
            budget.GetStatistics()!.CurrentAvailablePermits.ShouldBe(1);
            budget.GetStatistics()!.TotalSuccessfulLeases.ShouldBe(0);

            var first = FlowMessage.Create((FlowValue)"first", CorrelationId.New());
            (await firstInput.SendAsync(first, token)).ShouldBeTrue();
            var dispatched = await firstRequests.ReceiveAsync(token);
            dispatched.CorrelationId.ShouldNotBe(first.CorrelationId);
            attemptIds.Add(dispatched.CorrelationId!.Value).ShouldBeTrue();
            var echoed = await firstOutputs.ReceiveAsync(token);
            echoed.Value.ShouldBeSameAs(first.Value);
            echoed.CorrelationId.ShouldBe(first.CorrelationId);
            echoed.CausationId.ShouldBe(dispatched.MessageId);

            var waiting = FlowMessage.Create((FlowValue)"waiting", CorrelationId.New());
            (await Target("second", "Input").SendAsync(waiting, token)).ShouldBeTrue();
            while (budget.GetStatistics()!.CurrentQueuedCount != 1)
                await Task.Delay(5, token);
            var overflow = FlowMessage.Create((FlowValue)"overflow", CorrelationId.New());
            (await firstInput.SendAsync(overflow, token)).ShouldBeTrue();
            var rejected = await firstOutputs.ReceiveAsync(token);
            rejected.Value.ShouldBeOfType<FlowError>().Code.ShouldBe("rate_limit_rejected");
            rejected.CorrelationId.ShouldBe(overflow.CorrelationId);
            rejected.CausationId.ShouldBe(overflow.MessageId);
            budget.GetStatistics()!.CurrentQueuedCount.ShouldBe(1);

            var stop = app.StopAsync(token);
            stop.IsCompleted.ShouldBeFalse();
            var released = await secondRequests.ReceiveAsync(token);
            released.Value.ShouldBeSameAs(waiting.Value);
            released.CorrelationId.ShouldNotBe(waiting.CorrelationId);
            attemptIds.Add(released.CorrelationId!.Value).ShouldBeTrue();
            stop.IsCompleted.ShouldBeFalse();
            var feedback = released.WithValue((FlowValue)"handled during Stop");
            (await secondReply.SendAsync(feedback, token)).ShouldBeTrue();
            var handled = await secondOutputs.ReceiveAsync(token);
            handled.Value.ShouldBeSameAs(feedback.Value);
            handled.CorrelationId.ShouldBe(waiting.CorrelationId);
            handled.CausationId.ShouldBe(feedback.MessageId);
            await stop.WaitAsync(token);

            foreach (var sink in new[] { firstRequests, firstOutputs, secondRequests, secondOutputs })
                (await sink.OutputAvailableAsync(token)).ShouldBeFalse();
        }
    }

    private static JsonElement[] Messages(EnvironmentRunStatus status, string port) => status.Outputs!.Records
        .Where(record => record.Port == "main." + port).Select(record => record.Message!.Value).ToArray();

    private static ConfigurationRoot Configuration(string json) => (ConfigurationRoot)new ConfigurationBuilder()
        .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
}
