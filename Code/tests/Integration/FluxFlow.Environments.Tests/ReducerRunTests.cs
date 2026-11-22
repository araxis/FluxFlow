using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Execution;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class ReducerRunTests
{
    [Fact]
    public async Task Stop_waits_for_equality_and_drains_queued_input_before_rerun()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var comparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var config = WorkflowExecution.ReadConfiguration("""
            {"Workflows":{"main":{"reduce":{"Type":"reducer","Engine":"jsonata","InitialState":"0",
              "Reduce":"$state + $input","AreEqual":"$current = 2 ? $holdEquality($previous = $current) : $previous = $current"}}}}
            """);
        using var plugins = RunTestPlugins.WithBasicComponents();
        var services = new ServiceCollection();
        WorkflowExecution.Register(services, config, plugins);
        services.AddJsonataExpressionEngine(functions: new Dictionary<string, (Delegate, string?)>
        {
            ["holdEquality"] = ((bool equal) =>
            {
                comparing.SetResult();
                release.Wait(token);
                return equal;
            }, "<b:b>")
        });
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync(token);
        await app.StartAsync(token);
        var workflow = provider.GetRequiredKeyedService<FlowWorkflow>("main");

        for (var run = 0; run < 2; run++)
        {
            if (run == 1) await workflow.StartAsync(token);
            var input = workflow.Services.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.reduce.Input");
            var outputs = new BufferBlock<FlowMessage>();
            using var link = workflow.Services.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.reduce.Output")
                .LinkTo(outputs, new DataflowLinkOptions { PropagateCompletion = true });
            if (run == 0)
            {
                var first = Input(2);
                var second = Input(3);
                Task stopping;
                try
                {
                    (await input.SendAsync(first, token)).ShouldBeTrue();
                    await comparing.Task.WaitAsync(token);
                    (await input.SendAsync(second, token)).ShouldBeTrue();
                    stopping = workflow.StopAsync(token);
                    stopping.IsCompleted.ShouldBeFalse();
                    outputs.Count.ShouldBe(0);
                }
                finally { release.Set(); }
                await stopping;
                CheckResult(await outputs.ReceiveAsync(token), 2, first);
                CheckResult(await outputs.ReceiveAsync(token), 5, second);
            }
            else
            {
                var fresh = Input(1);
                (await input.SendAsync(fresh, token)).ShouldBeTrue();
                await workflow.StopAsync(token);
                CheckResult(await outputs.ReceiveAsync(token), 1, fresh);
            }
            (await outputs.OutputAvailableAsync(token)).ShouldBeFalse();
        }

        static FlowMessage Input(int delta) =>
            FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(delta)), CorrelationId.New());

        static void CheckResult(FlowMessage result, int state, FlowMessage trigger)
        {
            result.Value.Json.GetInt32().ShouldBe(state);
            result.CausationId.ShouldBe(trigger.MessageId);
            result.CorrelationId.ShouldBe(trigger.CorrelationId);
            result.TraceId.ShouldBe(trigger.TraceId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Errors_and_unchanged_candidates_preserve_state_and_rerun_starts_fresh(bool equalityFailure)
    {
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var reduce = equalityFailure ? "$state + $input" : "$input < 0 ? $error('bad input') : $state + $input";
        var equal = equalityFailure ? "$current = 0 ? $error('bad equality') : $previous = $current" : "$previous = $current";
        var request = new EnvironmentRunRequest(JsonSerializer.Serialize(new
        {
            Workflows = new { main = new
            {
                tick = new { Type = "scheduler", Interval = "00:00:00.020", Output = "source.Input" },
                source = new { Type = "source", Values = "[1,0,-1,2]", Output = "reduce.Input" },
                reduce = new { Type = "reducer", Engine = "jsonata", InitialState = "0", Reduce = reduce, AreEqual = equal }
            } }
        }), new());
        var original = JsonSerializer.Serialize(request);
        (await runner.CheckAsync(request, token)).Success.ShouldBeTrue();
        Guid previous = Guid.Empty;
        string? retained = null;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var id = Guid.NewGuid();
            try
            {
                (await runner.StartAsync(id, request, token)).State.ShouldBe("Running");
                while ((await runner.GetAsync(id, token)).Outputs!.Records.Count(r => r.Port == "main.reduce.Output") < 3)
                    await Task.Delay(10, token);
                await runner.StopAsync(id, token);
                var stopped = await runner.GetAsync(id, token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var output = stopped.Outputs!.Records.Where(r => r.Port == "main.reduce.Output").Select(r => r.Message!.Value).ToArray();
                output.Length.ShouldBe(3);
                output[0].GetProperty("Value").GetInt32().ShouldBe(1);
                output[1].GetProperty("IsError").GetBoolean().ShouldBeTrue();
                output[2].GetProperty("Value").GetInt32().ShouldBe(3);
                if (retained is not null)
                    JsonSerializer.Serialize((await runner.GetAsync(previous, token)).Outputs).ShouldBe(retained);
                previous = id; retained = JsonSerializer.Serialize(stopped.Outputs);
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
        }
        JsonSerializer.Serialize(request).ShouldBe(original);
    }

    [Fact]
    public async Task Stop_during_active_generation_and_rerun_reset_state_without_changing_retained_outputs()
    {
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var logging = ExecutionLogTests.CreateFactory();
        var preparation = new EnvironmentPreparation([], []);
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var request = new EnvironmentRunRequest("""
            {"Workflows":{"main":{
              "tick":{"Type":"scheduler","Interval":"00:00:00.020","Output":"reduce.Input"},
              "reduce":{"Type":"reducer","Engine":"jsonata","InitialState":"0","Reduce":"$state + 1","AreEqual":"$previous = $current"}
            }}}
            """, new());
        var original = JsonSerializer.Serialize(request);
        Guid previous = Guid.Empty;
        string? retained = null;
        string? lastMessage = null;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var id = Guid.NewGuid();
            try
            {
                (await runner.StartAsync(id, request, token)).State.ShouldBe("Running");
                while ((await runner.GetAsync(id, token)).Outputs!.Records.Count(r => r.Port == "main.reduce.Output") < 3)
                    await Task.Delay(10, token);
                await runner.StopAsync(id, token);
                var stopped = await runner.GetAsync(id, token);
                stopped.State.ShouldBe("Stopped", stopped.Error);
                var output = stopped.Outputs!.Records.Where(r => r.Port == "main.reduce.Output").Select(r => r.Message!.Value).ToArray();
                output.Select(r => r.GetProperty("Value").GetInt32()).ShouldBe(Enumerable.Range(1, output.Length));
                var message = output[0].GetProperty("MessageId").GetString();
                message.ShouldNotBe(lastMessage);
                if (retained is not null)
                    JsonSerializer.Serialize((await runner.GetAsync(previous, token)).Outputs).ShouldBe(retained);
                previous = id; retained = JsonSerializer.Serialize(stopped.Outputs); lastMessage = message;
                await runner.StopAsync(id, token);
                JsonSerializer.Serialize((await runner.GetAsync(id, token)).Outputs).ShouldBe(retained);
            }
            finally { await runner.StopAsync(id, CancellationToken.None); }
        }
        JsonSerializer.Serialize(request).ShouldBe(original);
    }
}
