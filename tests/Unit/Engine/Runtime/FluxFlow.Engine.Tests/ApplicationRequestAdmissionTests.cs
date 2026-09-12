using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed partial class ApplicationRequestReplyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_deadline_or_cancellation_during_admission_releases_capacity_without_late_delivery(bool cancel)
    {
        var clock = new FakeTimeProvider();
        await using var runtime = new ApplicationPortRuntimeBuilder(requestCapacity: 1, timeProvider: clock)
            .AddInput(Input, capacity: 1).AddOutput(Output, replies: true).Build();
        var requests = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 1 });
        var replies = new BufferBlock<FlowMessage>();
        var occupied = FlowMessage.Create(FlowValue.From("occupied"));
        var accepted = FlowMessage.Create(FlowValue.From("accepted"));
        requests.Post(occupied).ShouldBeTrue();
        var attachment = await runtime.AttachInputAsync(Input, requests);
        using var output = runtime.AttachOutput(Output, replies);
        using var cancellation = new CancellationTokenSource();
        try
        {
            (await runtime.SendAsync(Input, accepted)).IsAccepted.ShouldBeTrue();
            var expired = runtime.SendAndReceiveAsync<string, string>(Input, Output,
                FlowMessage.Create("expired"), TestTimeout, cancellation.Token);
            expired.IsCompleted.ShouldBeFalse();
            if (cancel)
            {
                cancellation.Cancel();
                (await Should.ThrowAsync<OperationCanceledException>(() => expired.WaitAsync(TestTimeout)))
                    .CancellationToken.ShouldBe(cancellation.Token);
            }
            else
            {
                clock.Advance(TestTimeout);
                var result = await expired.WaitAsync(TestTimeout);
                result.Status.ShouldBe(PortRequestStatus.TimedOut);
                result.Response.ShouldBeNull();
            }

            // Request capacity is released before downstream capacity becomes available.
            var next = runtime.SendAndReceiveAsync<string, string>(Input, Output,
                FlowMessage.Create("next"), TestTimeout);
            next.IsCompleted.ShouldBeFalse();
            (await requests.ReceiveAsync().WaitAsync(TestTimeout)).ShouldBeSameAs(occupied);
            (await requests.ReceiveAsync().WaitAsync(TestTimeout)).ShouldBeSameAs(accepted);
            var sent = await requests.ReceiveAsync().WaitAsync(TestTimeout);
            sent.Value.ShouldNotBeNull().Deserialize<string>().ShouldBe("next");
            sent.CorrelationId.ShouldNotBeNull();
            replies.Post(sent.With(FlowValue.From("received"))).ShouldBeTrue();
            var received = await next.WaitAsync(TestTimeout);
            received.Status.ShouldBe(PortRequestStatus.Received);
            received.Response.ShouldNotBeNull().Value.ShouldBe("received");
            received.Response.CorrelationId.ShouldBe(sent.CorrelationId);
            requests.TryReceive(out _).ShouldBeFalse();
            runtime.Complete();
            await runtime.Completion.WaitAsync(TestTimeout);
            runtime.Status.Ports.Single(port => port.Address == Input).PendingMessages.ShouldBe(0);
        }
        finally
        {
            await runtime.DisposeAsync();
            await attachment.DisposeAsync();
            requests.Complete();
            replies.Complete();
        }
    }
}
