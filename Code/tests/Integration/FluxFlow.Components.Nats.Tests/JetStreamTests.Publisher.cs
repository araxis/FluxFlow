using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Nats.JetStream;
using FluxFlow.Core.Messaging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Nats.Tests;

public sealed partial class JetStreamTests
{
    public enum PublishRecovery { ReconnectAfterTimeout, ReconnectWhilePending, StopWhilePending }

    [Theory]
    [InlineData(PublishRecovery.ReconnectAfterTimeout)]
    [InlineData(PublishRecovery.ReconnectWhilePending)]
    [InlineData(PublishRecovery.StopWhilePending)]
    public async Task Missing_publish_ack_preserves_correlation_and_does_not_replay(PublishRecovery recovery)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        await using var observer = new NatsConnection(Options);
        var js = new NatsJSContext(observer);
        var config = new StreamConfig("TRIAL", ["trial.data"]) { NoAck = true };
        var stream = await js.CreateStreamAsync(config, token);
        // Count every publish, including attempts the stream would deduplicate.
        await using var attempts = await observer.SubscribeCoreAsync<byte[]>("trial.*", cancellationToken: token);
        await observer.PingAsync(token);

        var options = Stream with { CreateStream = false };
        await using var session = new NatsSession(Options);
        await using var publisher = new JetStreamPublisher(session, options);
        var results = Capture(publisher.Output);
        var elapsed = Stopwatch.StartNew();
        var first = await Send(publisher, "same-id", "AQ==");
        await stream.RefreshAsync(token);
        while (stream.Info.State.Messages == 0)
        {
            await Task.Delay(20, token);
            await stream.RefreshAsync(token);
        }
        stream.Info.State.Messages.ShouldBe(1);
        var sequence = stream.Info.State.LastSeq;
        results.Count.ShouldBe(0);
        var stopWhilePending = recovery == PublishRecovery.StopWhilePending;
        if (stopWhilePending) publisher.Complete();
        if (recovery == PublishRecovery.ReconnectWhilePending) await Reconnect();
        var failed = await results.ReceiveAsync(TimeSpan.FromSeconds(5), token);
        CheckLineage(failed, first);
        failed.Error.ShouldNotBeNull().Code.ShouldBe("node.processing_failed");
        if (recovery != PublishRecovery.ReconnectWhilePending)
            elapsed.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(options.Limits.TimeoutSeconds - 0.1));

        if (stopWhilePending)
        {
            await publisher.Completion.WaitAsync(token);
            (await publisher.Input.SendAsync(first, token)).ShouldBeFalse();
            await session.DisposeAsync();
        }
        if (recovery == PublishRecovery.ReconnectAfterTimeout) await Reconnect();

        await js.UpdateStreamAsync(config with { NoAck = false }, token);
        await using var rerunSession = stopWhilePending ? new NatsSession(Options) : null;
        await using var rerun = rerunSession is null ? null : new JetStreamPublisher(rerunSession, options);
        var current = rerun ?? publisher;
        var output = rerun is null ? results : Capture(rerun.Output);
        var resend = await Send(current, "same-id", "Ag==");
        await CheckAck(resend, sequence, duplicate: true);
        await stream.RefreshAsync(token);
        stream.Info.State.Messages.ShouldBe(1);

        var fresh = await Send(current, "fresh-id", "Aw==");
        await CheckAck(fresh, sequence + 1, duplicate: false);
        current.Complete();
        await current.Completion.WaitAsync(token);
        await stream.RefreshAsync(token);
        stream.Info.State.Messages.ShouldBe(2);
        output.Count.ShouldBe(0);
        results.Count.ShouldBe(0);
        await observer.PublishAsync("trial.end", Array.Empty<byte>(), cancellationToken: token);
        (await attempts.Msgs.ReadAsync(token)).Subject.ShouldBe("trial.end");

        async Task Reconnect()
        {
            var native = await session.OpenAsync(token);
            var reconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            native.ConnectionOpened += (_, _) => { reconnected.TrySetResult(); return ValueTask.CompletedTask; };
            await native.ReconnectAsync();
            await reconnected.Task.WaitAsync(token);
            await native.PingAsync(token);
        }

        async Task<FlowMessage> Send(JetStreamPublisher node, string id, string data)
        {
            var message = Payload with { Subject = "trial.data", DataBase64 = data, Headers = new() { ["Nats-Msg-Id"] = [id] } };
            var input = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(message)), CorrelationId.New());
            (await node.Input.SendAsync(input, token)).ShouldBeTrue();
            var attempt = await attempts.Msgs.ReadAsync(token);
            attempt.Subject.ShouldBe(message.Subject);
            attempt.Headers!["Nats-Msg-Id"].ToString().ShouldBe(id);
            Convert.ToBase64String(attempt.Data!).ShouldBe(data);
            return input;
        }

        async Task CheckAck(FlowMessage input, ulong expected, bool duplicate)
        {
            var response = await output.ReceiveAsync(token);
            CheckLineage(response, input);
            response.IsError.ShouldBeFalse(response.Error?.Message);
            var ack = response.Value.Json.Deserialize<JetStreamPublished>()!;
            ack.Stream.ShouldBe(config.Name);
            ack.Sequence.ShouldBe(expected);
            ack.Duplicate.ShouldBe(duplicate);
            ack.Message.DataBase64.ShouldBe(input.Value.Json.GetProperty("DataBase64").GetString());
        }

        static void CheckLineage(FlowMessage output, FlowMessage input)
        {
            output.CausationId.ShouldBe(input.MessageId);
            output.CorrelationId.ShouldBe(input.CorrelationId);
            output.TraceId.ShouldBe(input.TraceId);
        }
    }
}
