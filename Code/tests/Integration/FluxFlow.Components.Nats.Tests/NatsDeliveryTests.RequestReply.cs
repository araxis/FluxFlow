using System.Diagnostics;
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
    public async Task Responder_disconnect_after_receipt_expires_reply_without_replay(bool stop)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        const string subject = "service.echo";
        const string oldInbox = "_INBOX.reply-trial.old";
        const string freshInbox = "_INBOX.reply-trial.fresh";
        await using var requester = new NatsConnection(Options());
        await using var inboxes = await requester.SubscribeCoreAsync<byte[]>("_INBOX.reply-trial.*", cancellationToken: token);
        await requester.PingAsync(token);
        await using var session = new NatsSession(Options());
        await using var service = new NatsSubscriber(session, subject);
        var requests = Capture(service.Output);
        await service.StartAsync(token);
        var options = new NatsComponentOptions { TimeoutSeconds = 1 };
        await using var reply = new NatsReply(session, options);
        var sent = Capture(reply.Output);
        var original = await ReceiveCommand(requests, oldInbox, "AQ==");

        var native = await session.OpenAsync(token);
        var offline = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        native.OnConnectingAsync = async endpoint =>
        {
            offline.TrySetResult();
            await release.Task.WaitAsync(token);
            return endpoint;
        };
        try
        {
            await native.ReconnectAsync();
            await offline.Task.WaitAsync(token);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds + 1));
            var elapsed = Stopwatch.StartNew();
            (await reply.Input.SendAsync(original, budget.Token)).ShouldBeTrue();
            if (stop)
            {
                service.StopProduction();
                reply.Complete();
            }
            var error = await sent.ReceiveAsync(budget.Token);
            elapsed.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(options.TimeoutSeconds - 0.1));
            error.Error.ShouldNotBeNull().Code.ShouldBe("node.processing_failed");
            error.CausationId.ShouldBe(original.MessageId);
            error.TraceId.ShouldBe(original.TraceId);
            error.CorrelationId.ShouldBe(original.CorrelationId);
            if (stop)
            {
                await Task.WhenAll(reply.Completion, service.Completion).WaitAsync(budget.Token);
                (await reply.Input.SendAsync(original, token)).ShouldBeFalse();
            }
        }
        finally { release.TrySetResult(); }
        await native.PingAsync(token);
        if (stop) await session.DisposeAsync();

        await using var nextSession = stop ? new NatsSession(Options()) : null;
        await using var nextService = nextSession is null ? null : new NatsSubscriber(nextSession, subject);
        await using var nextReply = nextSession is null ? null : new NatsReply(nextSession, options);
        var current = nextReply ?? reply;
        var currentService = nextService ?? service;
        var currentOutput = nextReply is null ? sent : Capture(nextReply.Output);
        var currentRequests = nextService is null ? requests : Capture(nextService.Output);
        if (nextService is not null) await nextService.StartAsync(token);
        var fresh = await ReceiveCommand(currentRequests, freshInbox, "AP+A");
        (await current.Input.SendAsync(fresh, token)).ShouldBeTrue();
        (await currentOutput.ReceiveAsync(token)).ShouldBeSameAs(fresh);
        var delivery = await inboxes.Msgs.ReadAsync(token);
        delivery.Subject.ShouldBe(freshInbox);
        delivery.Data.ShouldBe(new byte[] { 0, 255, 128 });
        delivery.Headers!["Result"].ToArray().ShouldBe(new[] { "one", "two" });

        current.Complete();
        currentService.StopProduction();
        await Task.WhenAll(current.Completion, currentService.Completion).WaitAsync(token);
        var currentConnection = await (nextSession ?? session).OpenAsync(token);
        await currentConnection.PingAsync(token);
        await requester.PublishAsync("_INBOX.reply-trial.end", Array.Empty<byte>(), cancellationToken: token);
        (await inboxes.Msgs.ReadAsync(token)).Subject.ShouldBe("_INBOX.reply-trial.end");
        currentOutput.Count.ShouldBe(0);
        sent.Count.ShouldBe(0);

        async Task<FlowMessage> ReceiveCommand(BufferBlock<FlowMessage> received, string inbox, string responseBytes)
        {
            await requester.PublishAsync(subject, new byte[] { 42 }, replyTo: inbox, cancellationToken: token);
            var request = await received.ReceiveAsync(token);
            var payload = request.Value.Json.Deserialize<NatsMessage>()!;
            payload.ReplyTo.ShouldBe(inbox);
            var response = payload with { DataBase64 = responseBytes, Headers = new() { ["Result"] = ["one", "two"] } };
            return FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(response)), CorrelationId.New());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disconnect_after_responder_receipt_never_replays_or_crosses_reply_inboxes(bool stop)
    {
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        await using var responder = new NatsConnection(Options());
        await using var attempts = await responder.SubscribeCoreAsync<byte[]>("trial.*", cancellationToken: token);
        await responder.PingAsync(token);
        await using var session = new NatsSession(Options());
        var native = await session.OpenAsync(token);
        // Capture a live socket so the request loses its connection only after the responder sees it.
        var socketReady = new TaskCompletionSource<INatsSocketConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        native.OnSocketAvailableAsync = socket =>
        {
            socketReady.TrySetResult(socket);
            return ValueTask.FromResult(socket);
        };
        await native.ReconnectAsync();
        var socket = await socketReady.Task.WaitAsync(token);
        await native.PingAsync(token);
        var offline = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var online = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        native.OnConnectingAsync = async endpoint =>
        {
            offline.TrySetResult();
            await release.Task.WaitAsync(token);
            return endpoint;
        };
        native.ConnectionOpened += (_, _) => { online.TrySetResult(); return ValueTask.CompletedTask; };
        var options = new NatsComponentOptions { TimeoutSeconds = 2 };
        await using var request = new NatsRequest(session, options);
        var output = Capture(request.Output);
        var original = Request("AP8=");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds + 1));
        (await request.Input.SendAsync(original, budget.Token)).ShouldBeTrue();
        var first = await attempts.Msgs.ReadAsync(budget.Token);
        first.ReplyTo.ShouldNotBeNullOrEmpty();
        first.Data.ShouldBe(new byte[] { 0, 255 });
        try
        {
            if (stop) request.Complete();
            await socket.DisposeAsync();
            await offline.Task.WaitAsync(budget.Token);
            await first.ReplyAsync(new byte[] { 1 }, cancellationToken: budget.Token);
            await responder.PingAsync(budget.Token);
            if (!stop) release.TrySetResult();
            var failure = await output.ReceiveAsync(budget.Token);
            failure.Error.ShouldNotBeNull().Code.ShouldBe("node.processing_failed");
            CheckLineage(failure, original);
            if (stop)
            {
                await request.Completion.WaitAsync(budget.Token);
                (await request.Input.SendAsync(original, token)).ShouldBeFalse();
            }
        }
        finally { release.TrySetResult(); }
        await online.Task.WaitAsync(token);
        await native.PingAsync(token);
        if (stop) await session.DisposeAsync();

        await using var nextSession = stop ? new NatsSession(Options()) : null;
        await using var rerun = nextSession is null ? null : new NatsRequest(nextSession, options);
        var current = rerun ?? request;
        var currentOutput = rerun is null ? output : Capture(rerun.Output);
        var fresh = Request("Av8=");
        (await current.Input.SendAsync(fresh, token)).ShouldBeTrue();
        var second = await attempts.Msgs.ReadAsync(token);
        second.Data.ShouldBe(new byte[] { 2, 255 });
        second.ReplyTo.ShouldNotBeNullOrEmpty();
        second.ReplyTo.ShouldNotBe(first.ReplyTo);
        await first.ReplyAsync(new byte[] { 3 }, cancellationToken: token);
        await second.ReplyAsync(new byte[] { 4, 0, 255 }, cancellationToken: token);
        var result = await currentOutput.ReceiveAsync(token);
        result.Error.ShouldBeNull();
        CheckLineage(result, fresh);
        var reply = result.Value.Json.Deserialize<NatsMessage>()!;
        reply.Subject.ShouldBe(second.ReplyTo);
        reply.DataBase64.ShouldBe("BAD/");
        current.Complete();
        await current.Completion.WaitAsync(token);
        // A replay would appear before this marker even if it produced no workflow output.
        await responder.PublishAsync("trial.end", Array.Empty<byte>(), cancellationToken: token);
        (await attempts.Msgs.ReadAsync(token)).Subject.ShouldBe("trial.end");
        currentOutput.Count.ShouldBe(0);
        output.Count.ShouldBe(0);

        static FlowMessage Request(string data) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(
            new NatsMessage { Subject = "trial.request", DataBase64 = data })), CorrelationId.New());

        static void CheckLineage(FlowMessage result, FlowMessage input)
        {
            result.CausationId.ShouldBe(input.MessageId);
            result.TraceId.ShouldBe(input.TraceId);
            result.CorrelationId.ShouldBe(input.CorrelationId);
        }
    }

    [Fact]
    public async Task Shared_request_connection_correlates_reversed_replies_by_inbox()
    {
        await using var server = new NatsSession(Options());
        await using var service = new NatsSubscriber(server, "lookup");
        var requests = Capture(service.Output);
        await service.StartAsync().WaitAsync(Deadline);
        await using var reply = new NatsReply(server);
        var sent = Capture(reply.Output);
        var missingInbox = Message(new() { Subject = "lookup", DataBase64 = "AQ==" });
        await reply.Input.SendAsync(missingInbox);
        var rejected = await sent.ReceiveAsync(Deadline);
        rejected.IsError.ShouldBeTrue();
        rejected.CausationId.ShouldBe(missingInbox.MessageId);
        await using var client = new NatsSession(Options());
        await using var first = new NatsRequest(client);
        await using var second = new NatsRequest(client);
        var firstOutput = Capture(first.Output);
        var secondOutput = Capture(second.Output);

        // Both requests deliberately share the same application-level correlation header.
        var payload = new NatsMessage { Subject = "lookup", Headers = new() { ["Correlation-Id"] = ["same"] } };
        var a = Message(payload with { DataBase64 = "AQ==" });
        var b = Message(payload with { DataBase64 = "Ag==" });
        await first.Input.SendAsync(a);
        var requestA = (await requests.ReceiveAsync(Deadline)).Value.Json.Deserialize<NatsMessage>()!;
        await second.Input.SendAsync(b);
        var requestB = (await requests.ReceiveAsync(Deadline)).Value.Json.Deserialize<NatsMessage>()!;
        requestA.ReplyTo.ShouldNotBeNullOrEmpty();
        requestB.ReplyTo.ShouldNotBeNullOrEmpty();
        requestA.ReplyTo.ShouldNotBe(requestB.ReplyTo);
        requestA.DataBase64.ShouldBe("AQ=="); requestB.DataBase64.ShouldBe("Ag==");
        first.Input.Complete(); // Graceful completion must drain the pending request.

        await Respond(requestB, "", b, secondOutput);
        await Respond(requestA, "AP+A", a, firstOutput);
        service.StopProduction(); reply.Input.Complete(); second.Input.Complete();
        await Task.WhenAll(service.Completion, reply.Completion, first.Completion, second.Completion).WaitAsync(Deadline);
        firstOutput.Count.ShouldBe(0); secondOutput.Count.ShouldBe(0);

        async Task Respond(NatsMessage request, string bytes, FlowMessage input, BufferBlock<FlowMessage> results)
        {
            var response = request with { DataBase64 = bytes, Headers = new() { ["Result"] = ["one", "two"] } };
            await reply.Input.SendAsync(Message(response));
            (await sent.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
            var result = await results.ReceiveAsync(Deadline);
            result.IsError.ShouldBeFalse(result.Error?.Message);
            result.CausationId.ShouldBe(input.MessageId);
            result.CorrelationId.ShouldBe(input.CorrelationId);
            var value = result.Value.Json.Deserialize<NatsMessage>()!;
            value.Subject.ShouldBe(request.ReplyTo);
            value.DataBase64.ShouldBe(bytes);
            value.Headers["Result"].ShouldBe(new[] { "one", "two" });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_reply_cannot_complete_a_later_request_or_a_fresh_run(bool rerun)
    {
        await using var server = new NatsSession(Options());
        await using var service = new NatsSubscriber(server, "slow");
        var deliveries = Capture(service.Output);
        await service.StartAsync().WaitAsync(Deadline);
        await using var reply = new NatsReply(server);
        var sent = Capture(reply.Output);
        await using var priorSession = new NatsSession(Options());
        await using var prior = new NatsRequest(priorSession, new() { TimeoutSeconds = 1 });
        var priorOutput = Capture(prior.Output);
        var expired = Message(new() { Subject = "slow", DataBase64 = "AQ==" });
        await prior.Input.SendAsync(expired);
        var staleDelivery = await deliveries.ReceiveAsync(Deadline);
        var timeout = await priorOutput.ReceiveAsync(Deadline);
        timeout.IsError.ShouldBeTrue();
        timeout.CausationId.ShouldBe(expired.MessageId);

        if (rerun)
        {
            prior.Input.Complete();
            await prior.Completion.WaitAsync(Deadline);
            await priorSession.DisposeAsync();
            (await prior.Input.SendAsync(expired)).ShouldBeFalse();
        }
        await using var nextSession = rerun ? new NatsSession(Options()) : null;
        await using var next = nextSession is null ? null : new NatsRequest(nextSession);
        var current = next ?? prior;
        var results = next is null ? priorOutput : Capture(next.Output);
        var fresh = Message(new() { Subject = "slow", DataBase64 = "Av8=" });
        await current.Input.SendAsync(fresh);
        var freshDelivery = await deliveries.ReceiveAsync(Deadline);
        freshDelivery.Value.Json.GetProperty("ReplyTo").GetString().ShouldNotBe(staleDelivery.Value.Json.GetProperty("ReplyTo").GetString());
        await reply.Input.SendAsync(staleDelivery);
        await reply.Input.SendAsync(freshDelivery);
        reply.Input.Complete(); // Both replies were admitted before Stop; drain them in order.
        (await reply.Input.SendAsync(staleDelivery)).ShouldBeFalse();
        (await sent.ReceiveAsync(Deadline)).Error.ShouldBeNull();
        (await sent.ReceiveAsync(Deadline)).Error.ShouldBeNull();
        var result = await results.ReceiveAsync(Deadline);
        result.Error.ShouldBeNull();
        result.CausationId.ShouldBe(fresh.MessageId);
        result.Value.Json.GetProperty("DataBase64").GetString().ShouldBe("Av8=");
        current.Input.Complete(); service.StopProduction();
        await Task.WhenAll(current.Completion, service.Completion, reply.Completion).WaitAsync(Deadline);
        priorOutput.Count.ShouldBe(0); results.Count.ShouldBe(0);
    }
}
