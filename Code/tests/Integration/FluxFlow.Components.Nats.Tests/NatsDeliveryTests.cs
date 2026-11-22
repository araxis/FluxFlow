using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluxFlow.Core.Messaging;
using NATS.Client.Core;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Nats.Tests;

public sealed partial class NatsDeliveryTests : IAsyncLifetime
{
    private readonly IContainer _broker = new ContainerBuilder("nats:2.11.8-alpine")
        .WithResourceMapping(Encoding.UTF8.GetBytes("""
            port: 4222
            http_port: 8222
            authorization {
              users: [
                { user: trial, password: trial-password },
                { user: restricted, password: trial-password, permissions: { publish: ["allowed.>"], subscribe: ["allowed.>", "_INBOX.>"] } }
              ]
            }
            """), "/etc/nats/trial.conf")
        .WithCommand("-c", "/etc/nats/trial.conf")
        .WithPortBinding(4222, true)
        .WithPortBinding(8222, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready")).Build();
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    public Task InitializeAsync() => _broker.StartAsync();
    public async Task DisposeAsync() => await _broker.DisposeAsync();
    private NatsOpts Options(string username = "trial", string password = "trial-password") => NatsOpts.Default with
    {
        Url = $"nats://{_broker.Hostname}:{_broker.GetMappedPublicPort(4222)}",
        AuthOpts = new() { Username = username, Password = password }, ConnectTimeout = TimeSpan.FromSeconds(2),
        CommandTimeout = TimeSpan.FromSeconds(2), RequestTimeout = TimeSpan.FromSeconds(2), MaxReconnectRetry = 3,
        ReconnectWaitMin = TimeSpan.FromMilliseconds(100), ReconnectWaitMax = TimeSpan.FromMilliseconds(100), DrainSubscriptionsOnDispose = false
    };
    private static FlowMessage Message(NatsMessage value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));
    private static BufferBlock<FlowMessage> Capture(ISourceBlock<FlowMessage> source)
    { var target = new BufferBlock<FlowMessage>(); source.LinkTo(target); return target; }

    [Fact]
    public async Task Publishing_distinguishes_missing_empty_and_null_headers_and_keeps_original_input()
    {
        await using var session = new NatsSession(Options());
        await using var subscriber = new NatsSubscriber(session, "plain");
        var received = Capture(subscriber.Output);
        await subscriber.StartAsync().WaitAsync(Deadline);
        await using var publisher = new NatsPublisher(session);
        var sent = Capture(publisher.Output);
        var invalid = JsonSerializer.SerializeToElement(new { Subject = "plain", DataBase64 = "AA==", Headers = (object?)null });
        await publisher.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(invalid)));
        (await sent.ReceiveAsync(Deadline)).Error.ShouldNotBeNull();
        foreach (var json in new[]
        {
            JsonSerializer.SerializeToElement(new { Subject = "plain", DataBase64 = "AP8B" }),
            JsonSerializer.SerializeToElement(new { Subject = "plain", DataBase64 = "AP8B", Headers = new { } })
        })
        {
            await publisher.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(json)));
            var result = await sent.ReceiveAsync(Deadline);
            result.Error.ShouldBeNull();
            result.Value.Json.GetRawText().ShouldBe(json.GetRawText());
            var delivery = (await received.ReceiveAsync(Deadline)).Value.Json;
            delivery.GetProperty("DataBase64").GetString().ShouldBe("AP8B");
            delivery.GetProperty("Headers").EnumerateObject().ShouldBeEmpty();
        }
        subscriber.StopProduction();
        publisher.Input.Complete();
        await Task.WhenAll(subscriber.Completion, publisher.Completion).WaitAsync(Deadline);
    }

    [Fact]
    public async Task Wildcard_delivery_preserves_bytes_and_repeated_headers_and_stops_cleanly()
    {
        await using var session = new NatsSession(Options());
        await using var subscriber = new NatsSubscriber(session, "sensors.*", options: new() { Capacity = 4 });
        var received = Capture(subscriber.Output); await subscriber.StartAsync().WaitAsync(Deadline);
        await using var publisher = new NatsPublisher(session); var sent = Capture(publisher.Output);
        var payload = new NatsMessage { Subject = "sensors.one", DataBase64 = "AP8BgA==", Headers = new() { ["X-Correlation-Id"] = ["one", "two"], ["Content-Type"] = ["application/octet-stream"] } };
        for (var i = 0; i < 30; i++)
        {
            (await publisher.Input.SendAsync(Message(payload))).ShouldBeTrue();
            (await sent.ReceiveAsync(Deadline)).Error.ShouldBeNull();
            var delivery = (await received.ReceiveAsync(Deadline)).Value.Json.Deserialize<NatsMessage>()!;
            delivery.Subject.ShouldBe(payload.Subject); delivery.DataBase64.ShouldBe(payload.DataBase64);
            delivery.Headers["X-Correlation-Id"].ShouldBe(new[] { "one", "two" });
        }
        subscriber.StopProduction(); await subscriber.Completion.WaitAsync(Deadline);
        publisher.Input.Complete(); await publisher.Completion.WaitAsync(Deadline);
        (await publisher.Input.SendAsync(Message(payload))).ShouldBeFalse();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Request_reply_uses_dynamic_subjects_and_preserves_response_bytes()
    {
        await using var session = new NatsSession(Options());
        await using var service = new NatsSubscriber(session, "service.echo");
        await using var reply = new NatsReply(session);
        service.Output.LinkTo(reply.Input); reply.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>());
        await service.StartAsync().WaitAsync(Deadline);
        await using var request = new NatsRequest(session, new() { TimeoutSeconds = 2 }); var responses = Capture(request.Output);
        for (var i = 0; i < 5; i++)
        {
            await request.Input.SendAsync(Message(new() { Subject = "service.echo", DataBase64 = "AQID", Headers = new() { ["Id"] = [i.ToString()] } }));
            var response = await responses.ReceiveAsync(Deadline); response.Error.ShouldBeNull();
            response.Value.Json.GetProperty("DataBase64").GetString().ShouldBe("AQID");
            response.Value.Json.GetProperty("Headers").GetProperty("Id")[0].GetString().ShouldBe(i.ToString());
        }
        service.StopProduction(); await service.Completion.WaitAsync(Deadline);
        reply.Input.Complete(); request.Input.Complete(); await Task.WhenAll(reply.Completion, request.Completion).WaitAsync(Deadline);
    }

    [Fact]
    public async Task Missing_responders_and_request_timeouts_are_errors_not_successful_empty_replies()
    {
        await using var session = new NatsSession(Options());
        await using var request = new NatsRequest(session, new() { TimeoutSeconds = 1 }); var responses = Capture(request.Output);
        var missing = Message(new() { Subject = "missing" });
        await request.Input.SendAsync(missing);
        var unavailable = await responses.ReceiveAsync(Deadline);
        unavailable.Error.ShouldNotBeNull();
        unavailable.CausationId.ShouldBe(missing.MessageId);
        await using var silent = new NatsSubscriber(session, "silent"); var deliveries = Capture(silent.Output);
        await silent.StartAsync().WaitAsync(Deadline);
        var waiting = Message(new() { Subject = "silent" });
        await request.Input.SendAsync(waiting);
        await deliveries.ReceiveAsync(Deadline);
        request.Input.Complete();
        (await request.Input.SendAsync(waiting)).ShouldBeFalse();
        var expired = await responses.ReceiveAsync(Deadline);
        expired.Error.ShouldNotBeNull();
        expired.CausationId.ShouldBe(waiting.MessageId);
        expired.Error.Message.ShouldNotBe(unavailable.Error.Message);
        silent.StopProduction(); await silent.Completion.WaitAsync(Deadline);
        await request.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Queue_group_distributes_each_message_to_one_member()
    {
        await using var session = new NatsSession(Options());
        await using var first = new NatsSubscriber(session, "jobs", "workers");
        await using var second = new NatsSubscriber(session, "jobs", "workers");
        var received = Capture(first.Output); second.Output.LinkTo(received);
        await first.StartAsync(); await second.StartAsync();
        await using var publisher = new NatsPublisher(session); publisher.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>());
        for (var i = 0; i < 20; i++) await publisher.Input.SendAsync(Message(new() { Subject = "jobs", DataBase64 = Convert.ToBase64String(BitConverter.GetBytes(i)) }));
        var ids = new HashSet<string>();
        for (var i = 0; i < 20; i++) ids.Add((await received.ReceiveAsync(Deadline)).Value.Json.GetProperty("DataBase64").GetString()!).ShouldBeTrue();
        first.StopProduction(); second.StopProduction(); publisher.Input.Complete();
        await Task.WhenAll(first.Completion, second.Completion, publisher.Completion).WaitAsync(Deadline);
        received.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_credentials_and_subscription_permission_fail_the_session_and_release_resources()
    {
        await using (var denied = new NatsSession(Options(password: "incorrect")))
            await Should.ThrowAsync<NatsException>(() => denied.OpenAsync(CancellationToken.None).WaitAsync(Deadline));
        await using var restricted = new NatsSession(Options(username: "restricted"));
        await using var subscriber = new NatsSubscriber(restricted, "forbidden");
        try { await subscriber.StartAsync().WaitAsync(Deadline); }
        catch (NatsException) { }
        var permission = await Should.ThrowAsync<NatsException>(() => subscriber.ProductionCompletion.WaitAsync(Deadline));
        permission.Message.ShouldContain("Permissions Violation");
        subscriber.StopProduction();
        try { await subscriber.Completion.WaitAsync(Deadline); } catch (Exception error) { error.ShouldNotBeOfType<TimeoutException>(); }
        await using var healthy = new NatsSession(Options()); await healthy.OpenAsync(CancellationToken.None).WaitAsync(Deadline);
    }

    [Fact]
    public async Task Exhausted_reconnect_attempts_fault_an_idle_subscriber_and_allow_cleanup()
    {
        await using var session = new NatsSession(Options());
        await using var subscriber = new NatsSubscriber(session, "idle");
        await subscriber.StartAsync().WaitAsync(Deadline);
        await _broker.StopAsync();
        await Should.ThrowAsync<NatsConnectionFailedException>(() => subscriber.ProductionCompletion.WaitAsync(Deadline));
        await using var publisher = new NatsPublisher(session);
        var sent = Capture(publisher.Output);
        await publisher.Input.SendAsync(Message(new() { Subject = "idle", DataBase64 = "AQ==" }));
        (await sent.ReceiveAsync(Deadline)).Error.ShouldNotBeNull();
        publisher.Input.Complete();
        await publisher.Completion.WaitAsync(Deadline);
        subscriber.StopProduction();
        try { await subscriber.Completion.WaitAsync(Deadline); }
        catch (Exception error) { error.ShouldNotBeOfType<TimeoutException>(); }
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("subscribe")]
    [InlineData("stop")]
    public async Task Reconnect_respects_command_deadlines_and_stop_without_replaying_deliveries(string operation)
    {
        await using var session = new NatsSession(Options());
        await using var existing = new NatsSubscriber(session, "recovery.*", options: new() { TimeoutSeconds = 1 });
        var received = Capture(existing.Output);
        await existing.StartAsync().WaitAsync(Deadline);
        var native = await session.OpenAsync(default);
        var reconnecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        native.OnConnectingAsync = async endpoint =>
        {
            reconnecting.TrySetResult();
            await release.Task.WaitAsync(Deadline);
            return endpoint;
        };
        native.ConnectionOpened += (_, _) => { recovered.TrySetResult(); return ValueTask.CompletedTask; };

        await using var publisher = new NatsPublisher(session, new() { TimeoutSeconds = 1 });
        var sent = Capture(publisher.Output);
        await using var candidate = new NatsSubscriber(session, "recovery.*", options: new() { TimeoutSeconds = 1 });
        var candidateRecords = Capture(candidate.Output);
        try
        {
            await native.ReconnectAsync();
            await reconnecting.Task.WaitAsync(Deadline);
            // The broker is alive, but this session cannot establish its replacement socket.
            if (operation == "publish")
            {
                await publisher.Input.SendAsync(Message(new() { Subject = "recovery.expired", DataBase64 = "AQ==" }));
                (await sent.ReceiveAsync(TimeSpan.FromSeconds(5))).Error.ShouldNotBeNull();
            }
            else if (operation == "subscribe")
            {
                var startup = Record.ExceptionAsync(() => candidate.StartAsync());
                (await startup.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeAssignableTo<OperationCanceledException>();
            }
            else
            {
                existing.StopProduction();
                await existing.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally { release.TrySetResult(); }

        await recovered.Task.WaitAsync(Deadline);
        await native.PingAsync().AsTask().WaitAsync(Deadline);
        // A stopped source must stay stopped when the session comes back online.
        if (operation == "stop") await candidate.StartAsync().WaitAsync(Deadline);
        var deliveries = operation == "stop" ? candidateRecords : received;
        await publisher.Input.SendAsync(Message(new() { Subject = "recovery.fresh", DataBase64 = "AP8=" }));
        (await sent.ReceiveAsync(Deadline)).Error.ShouldBeNull();
        var delivery = (await deliveries.ReceiveAsync(Deadline)).Value.Json.Deserialize<NatsMessage>()!;
        delivery.Subject.ShouldBe("recovery.fresh"); delivery.DataBase64.ShouldBe("AP8=");
        if (operation == "stop") { candidate.StopProduction(); await candidate.Completion.WaitAsync(Deadline); }
        existing.StopProduction(); publisher.Input.Complete();
        await Task.WhenAll(existing.Completion, publisher.Completion).WaitAsync(Deadline);
        candidateRecords.Count.ShouldBe(0); received.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Cancellation_fault_cancels_an_outstanding_request_without_waiting_for_its_timeout()
    {
        await using var session = new NatsSession(Options());
        await using var silent = new NatsSubscriber(session, "silent"); var received = Capture(silent.Output);
        await silent.StartAsync().WaitAsync(Deadline);
        await using var request = new NatsRequest(session, new() { TimeoutSeconds = 60 });
        request.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>());
        await request.Input.SendAsync(Message(new() { Subject = "silent" }));
        await received.ReceiveAsync(Deadline);
        request.Fault(new OperationCanceledException("Execution stop deadline expired."));
        await request.DisposeAsync().AsTask().WaitAsync(Deadline);
        silent.StopProduction(); await silent.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Cancellation_during_handshake_closes_the_socket_and_allows_a_fresh_connection()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        await using var session = new NatsSession(Options() with { Url = $"nats://127.0.0.1:{port}" });
        using var stop = new CancellationTokenSource();
        var opening = session.OpenAsync(stop.Token);
        using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(Deadline);
        stop.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => opening.WaitAsync(Deadline));
        (await accepted.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(Deadline)).ShouldBe(0);
        await using var healthy = new NatsSession(Options()); await healthy.OpenAsync(CancellationToken.None).WaitAsync(Deadline);
    }

    [Fact]
    public async Task Stop_releases_a_full_subscription_and_a_rerun_does_not_receive_old_records()
    {
        await using var session = new NatsSession(Options());
        await using var first = new NatsSubscriber(session, "bounded", options: new() { Capacity = 1 });
        await first.StartAsync();
        await using var sender = new NatsConnection(Options());
        for (var i = 0; i < 100; i++) await sender.PublishAsync("bounded", new byte[] { 1 });
        await sender.PingAsync();
        await first.Output.OutputAvailableAsync().WaitAsync(Deadline);
        first.StopProduction(); await first.ProductionCompletion.WaitAsync(Deadline);
        first.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>()); await first.Completion.WaitAsync(Deadline);
        await session.DisposeAsync();
        await sender.PublishAsync("bounded", new byte[] { 99 });
        await sender.PingAsync();
        await using var next = new NatsSession(Options());
        await using var rerun = new NatsSubscriber(next, "bounded"); var records = Capture(rerun.Output);
        await rerun.StartAsync().WaitAsync(Deadline);
        await sender.PublishAsync("bounded", new byte[] { 2 });
        (await records.ReceiveAsync(Deadline)).Value.Json.GetProperty("DataBase64").GetString().ShouldBe("Ag==");
        rerun.StopProduction(); await rerun.Completion.WaitAsync(Deadline); records.Count.ShouldBe(0);
    }
}
