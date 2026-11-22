using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Execution;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class RetryRunTests
{
    [Fact]
    public async Task Throwing_retry_predicate_emits_one_correlated_error_and_next_input_succeeds_across_runs()
    {
        const string json = """
            {"Workflows":{"main":{
              "retry":{"Type":"retry","Engine":"jsonata","ShouldRetry":"$ = 'reply-first' ? $error('predicate failed') : false",
                "MaxRetryAttempts":3,"Delay":"00:00:00","AttemptTimeout":"00:00:05","Request":"worker.Input","Reply":"worker.Output"},
              "worker":{"Type":"mapper","Engine":"jsonata","Expression":"'reply-' & $"}
            }}}
            """;
        using var config = WorkflowExecution.ReadConfiguration(json);
        using var plugins = RunTestPlugins.WithBasicComponents();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var attemptIds = new HashSet<CorrelationId>();
        FlowMessage? lateReply = null;
        for (var run = 0; run < 2; run++)
        {
            var services = new ServiceCollection();
            WorkflowExecution.Register(services, config, plugins);
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            await app.StartAsync(token);
            var ports = provider.GetWorkflowServices("main");
            var input = ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.retry.Input");
            var reply = ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.retry.Reply");
            var requests = new BufferBlock<FlowMessage>();
            var outputs = new BufferBlock<FlowMessage>();
            var link = new DataflowLinkOptions { PropagateCompletion = true };
            using var requestLink = ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.retry.Request").LinkTo(requests, link);
            using var outputLink = ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.retry.Output").LinkTo(outputs, link);
            if (lateReply is not null)
                (await reply.SendAsync(lateReply, token)).ShouldBeTrue();

            foreach (var value in new[] { "first", "second" })
            {
                var original = FlowMessage.Create((FlowValue)value, CorrelationId.New());
                (await input.SendAsync(original, token)).ShouldBeTrue();
                var request = await requests.ReceiveAsync(token);
                request.Value.Json.GetString().ShouldBe(value);
                request.TraceId.ShouldBe(original.TraceId);
                request.CorrelationId.ShouldNotBe(original.CorrelationId);
                attemptIds.Add(request.CorrelationId!.Value).ShouldBeTrue();
                var output = await outputs.ReceiveAsync(token);
                output.TraceId.ShouldBe(original.TraceId);
                output.CorrelationId.ShouldBe(original.CorrelationId);
                if (value == "first")
                {
                    output.Value.ShouldBeOfType<FlowError>().Message.ShouldContain("predicate failed");
                    output.CausationId.ShouldBe(original.MessageId);
                    lateReply = request.WithValue((FlowValue)"late");
                }
                else
                {
                    output.IsError.ShouldBeFalse();
                    output.Value.Json.GetString().ShouldBe("reply-second");
                }
            }

            await app.StopAsync(token);
            (await reply.SendAsync(lateReply!, token)).ShouldBeFalse();
            (await requests.OutputAvailableAsync(token)).ShouldBeFalse();
            (await outputs.OutputAvailableAsync(token)).ShouldBeFalse();
        }
    }
}
