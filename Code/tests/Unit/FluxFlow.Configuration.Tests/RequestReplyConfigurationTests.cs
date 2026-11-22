using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Components.RateLimit;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.RequestReply;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class RequestReplyConfigurationTests
{
    [Fact]
    public async Task Configuration_uses_rate_limit_runtime_and_rerun_has_fresh_correlation()
    {
        using var configuration = Config("""{"Type":"requestReply","MaxInFlight":2,"BufferCapacity":1,"ReplyTimeout":"00:00:05"}""");
        FlowMessage? previous = null;
        for (var run = 0; run < 2; run++)
        {
            await using var provider = new ServiceCollection().AddRequestReply().AddFluxFlowApplication(configuration).BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            await app.StartAsync();
            var services = provider.GetWorkflowServices("main");
            var component = services.GetRequiredKeyedService<IFlowNode>("main.exchange").ShouldBeOfType<RateLimit>();
            var input = services.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.exchange.Input");
            var ack = services.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.exchange.ACK");
            var nack = services.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.exchange.NACK");
            component.TerminalInputs.ShouldContain(ack);
            component.TerminalInputs.ShouldContain(nack);
            var requests = new BufferBlock<FlowMessage>(); var output = new BufferBlock<FlowMessage>();
            using var requestLink = component.Request.LinkTo(requests);
            using var outputLink = component.Output.LinkTo(output);
            var original = FlowMessage.Create((FlowValue)"work", CorrelationId.New());
            await input.SendAsync(original);
            var request = await requests.ReceiveAsync(TimeSpan.FromSeconds(5));
            if (previous is not null)
            {
                request.CorrelationId.ShouldNotBe(previous.CorrelationId);
                await nack.SendAsync(previous);
            }
            await ack.SendAsync(request.WithValue((FlowValue)"done"));
            var result = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
            result.CorrelationId.ShouldBe(original.CorrelationId);
            result.IsError.ShouldBeFalse();
            var completed = result.Value.ShouldBeOfType<RequestReplyResult>();
            completed.Outcome.ShouldBe(RequestReplyOutcome.ACK);
            completed.RequestId.ShouldBe(original.MessageId.ToString());
            completed.Request.ShouldBeSameAs(original.Value);
            completed.Reply!.Json.GetString().ShouldBe("done");
            previous = request;
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            output.Count.ShouldBe(0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_with_pending_feedback_drains_or_cancels_without_leaving_open_ports(bool cancel)
    {
        using var configuration = Config("""{"Type":"requestReply","BufferCapacity":0,"ReplyTimeout":"00:01:00"}""");
        var provider = new ServiceCollection().AddRequestReply().AddFluxFlowApplication(configuration).BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        using var cancellation = new CancellationTokenSource();
        try
        {
            await app.StartAsync();
            var component = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.exchange").ShouldBeOfType<RateLimit>();
            var requests = new BufferBlock<FlowMessage>();
            var results = new BufferBlock<FlowMessage>();
            using var requestLink = component.Request.LinkTo(requests);
            using var resultLink = component.Output.LinkTo(results);
            await component.Input.SendAsync(FlowMessage.Create((FlowValue)"pending"));
            var request = await requests.ReceiveAsync(TimeSpan.FromSeconds(5));
            var postponed = component.Input.SendAsync(FlowMessage.Create((FlowValue)"not accepted"));
            postponed.IsCompleted.ShouldBeFalse();
            var stop = app.StopAsync(cancellation.Token);
            if (cancel)
            {
                cancellation.Cancel();
                await Should.ThrowAsync<OperationCanceledException>(() => stop.WaitAsync(TimeSpan.FromSeconds(5)));
                await Should.ThrowAsync<OperationCanceledException>(() => component.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
                results.Count.ShouldBe(0);
            }
            else
            {
                stop.IsCompleted.ShouldBeFalse();
                (await component.Nack.SendAsync(request.WithValue((FlowValue)"rejected during drain"))).ShouldBeTrue();
                await stop.WaitAsync(TimeSpan.FromSeconds(5));
                (await results.ReceiveAsync(TimeSpan.FromSeconds(5))).Value.ShouldBeOfType<RequestReplyResult>()
                    .Outcome.ShouldBe(RequestReplyOutcome.NACK);
            }
            (await postponed.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
            (await component.Reply.SendAsync(request)).ShouldBeFalse();
            (await component.Nack.SendAsync(request)).ShouldBeFalse();
        }
        finally
        {
            if (cancel && cancellation.IsCancellationRequested)
                await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            else await provider.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("MaxInFlight", "0")]
    [InlineData("BufferCapacity", "-1")]
    [InlineData("ReplyTimeout", "00:00:00")]
    [InlineData("ReplyTimeout", "-00:00:00.001")]
    [InlineData("TimeoutBehavior", "99")]
    public void Invalid_settings_fail_at_registration(string name, string value)
    {
        using var configuration = Config("""{"Type":"requestReply"}""");
        configuration[$"Workflows:main:exchange:{name}"] = value;
        using var provider = new ServiceCollection().AddRequestReply().BuildServiceProvider();
        Should.Throw<ArgumentException>(() => provider.GetRequiredKeyedService<ComponentConfigurationCheck>("requestReply")(
            configuration, configuration.GetSection("Workflows:main:exchange")));
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddRequestReply().AddFluxFlowApplication(configuration))
            .Message.ShouldContain(name);
    }

    private static ConfigurationRoot Config(string component) => (ConfigurationRoot)new ConfigurationBuilder()
        .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("{\"Workflows\":{\"main\":{\"exchange\":" + component + "}}}"))).Build();
}
