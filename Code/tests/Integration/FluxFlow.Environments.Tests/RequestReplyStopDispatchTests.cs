using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.RequestReply;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class RequestReplyStopDispatchTests
{
    [Fact]
    public async Task Timeout_stops_queued_dispatch_keeps_active_feedback_and_resets_on_rerun()
    {
        const string json = """
            {"Workflows":{"main":{"exchange":{"Type":"requestReply","MaxInFlight":2,
              "BufferCapacity":1,"TimeoutBehavior":"StopDispatch","ReplyTimeout":"00:00:04"}}}}
            """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = deadline.Token;
        var original = FlowMessage.Create((FlowValue)"first", CorrelationId.New());
        var second = FlowMessage.Create((FlowValue)"second", original.CorrelationId);
        var queued = FlowMessage.Create((FlowValue)"queued", original.CorrelationId);
        FlowMessage? expiredRequest = null;

        for (var run = 0; run < 2; run++)
        {
            await using var provider = new ServiceCollection().AddRequestReply()
                .AddFluxFlowApplication(config).BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            await app.StartAsync(token);
            var ports = provider.GetWorkflowServices("main");
            var input = ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.exchange.Input");
            var ack = ports.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.exchange.ACK");
            var requests = new BufferBlock<FlowMessage>();
            var results = new BufferBlock<FlowMessage>();
            var link = new DataflowLinkOptions { PropagateCompletion = true };
            using var requestLink = ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.exchange.Request").LinkTo(requests, link);
            using var outputLink = ports.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.exchange.Output").LinkTo(results, link);
            (await input.SendAsync(original, token)).ShouldBeTrue();
            var firstRequest = await requests.ReceiveAsync(token);
            firstRequest.Value.ShouldBeSameAs(original.Value);
            var pending = firstRequest;
            var expected = original;
            if (run == 0)
            {
                expiredRequest = firstRequest;
                // Stagger the two real reply deadlines so the second can ACK after the first expires.
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                (await input.SendAsync(second, token)).ShouldBeTrue();
                pending = await requests.ReceiveAsync(token);
                pending.Value.ShouldBeSameAs(second.Value);
                expected = second;
                (await input.SendAsync(queued, token)).ShouldBeTrue();

                var stopped = new[] { await results.ReceiveAsync(token), await results.ReceiveAsync(token) };
                var timeout = ResultFor(stopped, original, "Timeout");
                timeout.GetProperty("Reply").ValueKind.ShouldBe(JsonValueKind.Null);
                var skipped = ResultFor(stopped, queued, "NotDispatched");
                skipped.GetProperty("Reply").ValueKind.ShouldBe(JsonValueKind.Null);
                skipped.GetProperty("Elapsed").Deserialize<TimeSpan>().ShouldBe(TimeSpan.Zero);
            }
            else
            {
                firstRequest.CorrelationId.ShouldNotBe(expiredRequest!.CorrelationId);
                (await ack.SendAsync(expiredRequest.WithValue((FlowValue)"stale"), token)).ShouldBeTrue();
            }

            var feedback = pending.WithValue((FlowValue)"handled");
            (await ack.SendAsync(feedback, token)).ShouldBeTrue();
            var output = await results.ReceiveAsync(token);
            ResultFor([output], expected, "ACK").GetProperty("Reply").GetString().ShouldBe("handled");
            output.CausationId.ShouldBe(feedback.MessageId);
            await app.StopAsync(token);
            (await ack.SendAsync(pending, token)).ShouldBeFalse();
            (await requests.OutputAvailableAsync(token)).ShouldBeFalse();
            (await results.OutputAvailableAsync(token)).ShouldBeFalse();
        }
    }

    private static JsonElement ResultFor(FlowMessage[] outputs, FlowMessage input, string outcome)
    {
        var output = outputs.Single(message => message.Value.Json.GetProperty("RequestId").GetString() == input.MessageId.ToString());
        output.IsError.ShouldBeFalse();
        output.CorrelationId.ShouldBe(input.CorrelationId);
        output.TraceId.ShouldBe(input.TraceId);
        var result = output.Value.Json;
        result.GetProperty("Outcome").GetString().ShouldBe(outcome);
        result.GetProperty("Request").GetRawText().ShouldBe(input.Value.Json.GetRawText());
        return result;
    }
}
