using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class CanonicalFanoutTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public async Task Canonical_fanout_shares_messages_values_and_headers_without_cloning(int targetCount)
    {
        var payload = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { Value = "payload" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var headers = new Dictionary<string, string> { ["source"] = "original" };
        var messages = Enumerable.Range(0, 3)
            .Select(_ => FlowMessage.Create(payload, headers: headers))
            .ToArray();
        headers["source"] = "mutated";
        var targets = Enumerable.Range(0, targetCount)
            .Select(_ => new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 4 }))
            .ToArray();
        await using var output = new FlowOutput<FlowMessage>();
        var links = targets.Select(target => output.LinkTo(target,
            new DataflowLinkOptions { PropagateCompletion = true })).ToArray();

        try
        {
            foreach (var message in messages)
                (await output.SendAsync(message).AsTask().WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue();
            output.Complete();

            foreach (var target in targets)
            {
                foreach (var expected in messages)
                {
                    var actual = await target.ReceiveAsync(TimeSpan.FromSeconds(30));
                    actual.ShouldBeSameAs(expected);
                    actual.Value.ShouldBeSameAs(payload);
                    actual.Headers.ShouldBeSameAs(expected.Headers);
                    actual.Headers["source"].ShouldBe("original");
                    actual.Value!.Json.GetProperty("value").GetString().ShouldBe("payload");
                }
                target.TryReceive(out _).ShouldBeFalse();
                await target.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            }
            await output.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            output.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally
        {
            foreach (var link in links)
                link.Dispose();
        }
    }
}
