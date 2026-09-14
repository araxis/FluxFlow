using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed partial class ApplicationRequestReplyTests
{
    private static readonly ApplicationAddress Input = ApplicationAddress.WorkflowPort("Main", "Processor", "Input");
    private static readonly ApplicationAddress Output = ApplicationAddress.WorkflowPort("Main", "Processor", "Output");
    private static readonly ApplicationAddress OtherOutput = ApplicationAddress.WorkflowPort("Main", "Other", "Output");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Canonical_requests_sharing_a_trace_match_only_their_exchange_and_expected_output()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input).AddOutput(Output).AddOutput(OtherOutput, discardUnmatched: true).Build();
        var requests = new BufferBlock<FlowMessage>();
        var replies = new BufferBlock<FlowMessage>();
        var otherReplies = new BufferBlock<FlowMessage>();
        await using var builder = runtime.CreateRevision("requests")
            .ReplaceInput(Input, requests).AttachOutput(Output, replies)
            .AttachOutput(OtherOutput, otherReplies);
        await using var revision = builder.Build();
        await using var lease = await revision.ActivateAsync();
        var request = FlowMessage.Create<FlowValue>((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("request", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var first = runtime.SendAndReceiveAsync<FlowValue, FlowValue>(Input, Output, request, TestTimeout);
        var second = runtime.SendAndReceiveAsync<FlowValue, FlowValue>(Input, Output, request, TestTimeout);
        var firstSent = await requests.ReceiveAsync().WaitAsync(TestTimeout);
        var secondSent = await requests.ReceiveAsync().WaitAsync(TestTimeout);
        firstSent.TraceId.ShouldBe(secondSent.TraceId);
        firstSent.CorrelationId.ShouldNotBe(secondSent.CorrelationId);
        firstSent.Value.ShouldBeSameAs(request.Value);
        firstSent.Headers.ShouldBeSameAs(request.Headers);

        var wrongAddressProcessed = runtime.ReceiveAsync<FlowValue>(OtherOutput, TestTimeout);
        otherReplies.Post(firstSent).ShouldBeTrue();
        (await wrongAddressProcessed).Status.ShouldBe(PortReceiveStatus.Received);
        var wrongCorrelationProcessed = runtime.ReceiveAsync<FlowValue>(Output, TestTimeout);
        replies.Post(request.ConvertValue(request.Value)).ShouldBeTrue();
        (await wrongCorrelationProcessed).Status.ShouldBe(PortReceiveStatus.Received);
        first.IsCompleted.ShouldBeFalse();
        second.IsCompleted.ShouldBeFalse();

        var secondReply = secondSent.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("second", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        replies.Post(secondReply).ShouldBeTrue();
        var secondResult = await second.WaitAsync(TestTimeout);
        secondResult.Status.ShouldBe(PortRequestStatus.Received);
        secondResult.Response.ShouldNotBeNull().Value.ShouldBeSameAs(secondReply.Value);
        secondResult.Response.MessageId.ShouldBe(secondReply.MessageId);
        var duplicateProcessed = runtime.ReceiveAsync<FlowValue>(Output, TestTimeout);
        replies.Post(secondReply).ShouldBeTrue();
        (await duplicateProcessed).Status.ShouldBe(PortReceiveStatus.Received);
        first.IsCompleted.ShouldBeFalse();

        var firstReply = firstSent.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("first", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        replies.Post(firstReply).ShouldBeTrue();
        var firstResult = await first.WaitAsync(TestTimeout);
        firstResult.Status.ShouldBe(PortRequestStatus.Received);
        firstResult.Response.ShouldNotBeNull().Value.ShouldBeSameAs(firstReply.Value);
        firstResult.Response.CorrelationId.ShouldBe(firstSent.CorrelationId);
    }

    [Fact]
    public async Task Finite_deadline_expires_and_releases_request_capacity()
    {
        var clock = new FakeTimeProvider();
        await using var runtime = new ApplicationPortRuntimeBuilder(requestCapacity: 1, timeProvider: clock)
            .AddInput(Input).AddOutput(Output).Build();
        var requests = new BufferBlock<FlowMessage>();
        var replies = new BufferBlock<FlowMessage>();
        await using var input = await runtime.AttachInputAsync(Input, requests);
        using var output = runtime.AttachOutput(Output, replies);
        var first = runtime.SendAndReceiveAsync<string, string>(Input, Output, FlowMessage.Create("first"), TestTimeout);
        var firstSent = await requests.ReceiveAsync().WaitAsync(TestTimeout);
        var rejection = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await runtime.SendAndReceiveAsync<string, string>(Input, Output, FlowMessage.Create("overflow"), TestTimeout));
        rejection.Message.ShouldBe("Request was not accepted: CapacityReached.");
        requests.Count.ShouldBe(0);

        clock.Advance(TestTimeout);
        (await first.WaitAsync(TestTimeout)).Status.ShouldBe(PortRequestStatus.TimedOut);
        var second = runtime.SendAndReceiveAsync<string, string>(Input, Output, FlowMessage.Create("second"), TestTimeout);
        var secondSent = await requests.ReceiveAsync().WaitAsync(TestTimeout);
        replies.Post(firstSent.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("late", new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ShouldBeTrue();
        replies.Post(secondSent.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("received", new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ShouldBeTrue();
        var result = await second.WaitAsync(TestTimeout);
        result.Status.ShouldBe(PortRequestStatus.Received);
        result.Response.ShouldNotBeNull().Value.ShouldBe("received");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Null_and_explicit_infinite_timeouts_preserve_caller_cancellation(bool explicitInfinite)
    {
        var clock = new FakeTimeProvider();
        await using var runtime = new ApplicationPortRuntimeBuilder(timeProvider: clock)
            .AddInput(Input).AddOutput(Output).Build();
        var requests = new BufferBlock<FlowMessage>();
        var replies = new BufferBlock<FlowMessage>();
        await using var input = await runtime.AttachInputAsync(Input, requests);
        using var output = runtime.AttachOutput(Output, replies);
        using var cancellation = new CancellationTokenSource();
        var pending = runtime.SendAndReceiveAsync<string, string>(Input, Output, FlowMessage.Create("request"),
            explicitInfinite ? Timeout.InfiniteTimeSpan : null, cancellation.Token);
        await requests.ReceiveAsync().WaitAsync(TestTimeout);

        clock.Advance(TimeSpan.FromDays(365));
        pending.IsCompleted.ShouldBeFalse();
        cancellation.Cancel();
        var failure = await Should.ThrowAsync<OperationCanceledException>(async () => await pending.WaitAsync(TestTimeout));
        failure.CancellationToken.ShouldBe(cancellation.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completing_or_disposing_the_host_settles_infinite_requests(bool dispose)
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input).AddOutput(Output).Build();
        var requests = new BufferBlock<FlowMessage>();
        var replies = new BufferBlock<FlowMessage>();
        await using var input = await runtime.AttachInputAsync(Input, requests);
        using var output = runtime.AttachOutput(Output, replies);
        var pending = runtime.SendAndReceiveAsync<string, string>(Input, Output, FlowMessage.Create("request"));
        await requests.ReceiveAsync().WaitAsync(TestTimeout);

        if (dispose) await runtime.DisposeAsync();
        else runtime.Complete();

        (await pending.WaitAsync(TestTimeout)).Status.ShouldBe(PortRequestStatus.OutputCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_must_succeed_before_a_canonical_reply_can_complete_its_request(bool fail)
    {
        var capture = new GatedCapture();
        var runtime = new ApplicationPortRuntimeBuilder(capture)
            .AddInput(Input).AddOutput(Output).Build();
        var requests = new BufferBlock<FlowMessage>();
        var replies = new BufferBlock<FlowMessage>();
        await using var builder = runtime.CreateRevision("capture")
            .ReplaceInput(Input, requests).AttachOutput(Output, replies);
        await using var revision = builder.Build();
        await using var lease = await revision.ActivateAsync();
        try
        {
            var pending = runtime.SendAndReceiveAsync<string, string>(Input, Output, FlowMessage.Create("request"));
            var sent = await requests.ReceiveAsync().WaitAsync(TestTimeout);
            var reply = sent.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("captured", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            replies.Post(reply).ShouldBeTrue();
            var captured = await capture.Entered.Task.WaitAsync(TestTimeout);
            captured.ShouldBeSameAs(reply);
            pending.IsCompleted.ShouldBeFalse();
            if (fail) capture.Release.TrySetException(capture.Failure);
            else capture.Release.TrySetResult();

            var result = await pending.WaitAsync(TestTimeout);
            if (fail)
            {
                result.Status.ShouldBe(PortRequestStatus.OutputCompleted);
                result.Response.ShouldBeNull();
                var rejection = await runtime.Rejections.ReceiveAsync().WaitAsync(TestTimeout);
                rejection.Reason.ShouldBe(ApplicationPortRejectionReason.OutputCaptureFailed);
                rejection.Exception.ShouldBeSameAs(capture.Failure);
                rejection.CorrelationId.ShouldBe(sent.CorrelationId);
            }
            else
            {
                result.Status.ShouldBe(PortRequestStatus.Received);
                result.Response.ShouldNotBeNull().Value.ShouldBe("captured");
            }
        }
        finally
        {
            capture.Release.TrySetResult();
            if (fail && capture.Entered.Task.IsCompletedSuccessfully)
            {
                var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
                    await runtime.DisposeAsync().AsTask().WaitAsync(TestTimeout));
                failure.ShouldBeSameAs(capture.Failure);
            }
            else await runtime.DisposeAsync();
        }
    }

    [Fact]
    public async Task Unavailable_output_is_reported_without_sending_a_request()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input).AddOutput(Output).Build();
        var requests = new BufferBlock<FlowMessage>();
        await using var input = await runtime.AttachInputAsync(Input, requests);

        var result = await runtime.SendAndReceiveAsync<string, string>(Input, Output, FlowMessage.Create("request"));

        result.Status.ShouldBe(PortRequestStatus.OutputUnavailable);
        result.Response.ShouldBeNull();
        requests.Count.ShouldBe(0);
    }

    private sealed class GatedCapture : IApplicationOutputCaptureResolver, IApplicationOutputCapture
    {
        public TaskCompletionSource<FlowMessage> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public InvalidOperationException Failure { get; } = new("Capture failed.");

        public IApplicationOutputCapture? Resolve(ApplicationAddress address)
            => address == Output ? this : null;

        public async ValueTask CaptureAsync(FlowMessage message, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(message);
            await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
