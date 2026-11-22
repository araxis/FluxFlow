using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using NATS.Client.Core;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Nats.Tests;

public sealed partial class NatsDeliveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queue_group_preserves_deliveries_across_rejoin_or_stop_and_rerun(bool rerun)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        const string subject = "jobs.recovery";
        const string group = "workers";
        using var monitor = new HttpClient { BaseAddress = new($"http://{_broker.Hostname}:{_broker.GetMappedPublicPort(8222)}") };
        await using var publisher = new NatsConnection(Options());
        await using var firstSession = new NatsSession(Options());
        await using var secondSession = new NatsSession(Options());
        await using var first = new NatsSubscriber(firstSession, subject, group);
        await using var second = new NatsSubscriber(secondSession, subject, group);
        var firstOutput = Capture(first.Output);
        var secondOutput = Capture(second.Output);
        await first.StartAsync(token);
        await second.StartAsync(token);
        await WaitForMembers(2);
        await CheckBatch(0, firstOutput, secondOutput);

        await using var nextSession = rerun ? new NatsSession(Options()) : null;
        await using var next = nextSession is null ? null : new NatsSubscriber(nextSession, subject, group);
        var returningOutput = next is null ? firstOutput : Capture(next.Output);
        var native = await firstSession.OpenAsync(token);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        native.OnConnectingAsync = async endpoint =>
        {
            await release.Task.WaitAsync(token);
            return endpoint;
        };
        try
        {
            await native.ReconnectAsync();
            await WaitForMembers(1);
            await CheckBatch(16, secondOutput);
            if (next is not null)
            {
                first.StopProduction();
                await first.Completion.WaitAsync(token);
                await next.StartAsync(token);
            }
        }
        finally { release.TrySetResult(); }

        await native.PingAsync(token);
        await WaitForMembers(2);
        await CheckBatch(32, returningOutput, secondOutput);
        second.StopProduction();
        await second.Completion.WaitAsync(token);
        await WaitForMembers(1);
        await CheckBatch(48, returningOutput);
        (next ?? first).StopProduction();
        await (next ?? first).Completion.WaitAsync(token);
        await WaitForMembers(0);
        firstOutput.Count.ShouldBe(0);
        secondOutput.Count.ShouldBe(0);
        returningOutput.Count.ShouldBe(0);

        async Task WaitForMembers(int expected)
        {
            while (true)
            {
                var stats = await monitor.GetFromJsonAsync<JsonElement>("/subsz?subs=1", token);
                var count = stats.TryGetProperty("subscriptions_list", out var subscriptions)
                    ? subscriptions.EnumerateArray().Count(s => s.GetProperty("subject").GetString() == subject
                        && s.GetProperty("qgroup").GetString() == group)
                    : 0;
                if (count == expected) return;
                await Task.Delay(20, token);
            }
        }

        async Task CheckBatch(int start, params BufferBlock<FlowMessage>[] outputs)
        {
            var expected = Enumerable.Range(start, 16).ToArray();
            foreach (var id in expected)
                await publisher.PublishAsync(subject, new byte[] { (byte)id }, cancellationToken: token);
            await publisher.PingAsync(token);
            while (outputs.Sum(output => output.Count) < expected.Length)
                await Task.Delay(10, token);
            var actual = new List<int>();
            foreach (var output in outputs)
                while (output.TryReceive(out var message))
                {
                    message.Error.ShouldBeNull();
                    actual.Add(message.Value.Json.GetProperty("DataBase64").GetBytesFromBase64().Single());
                }
            actual.Order().ShouldBe(expected);
        }
    }
}
