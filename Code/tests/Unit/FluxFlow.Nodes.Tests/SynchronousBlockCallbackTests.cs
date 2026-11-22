using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

[Collection(ProcessingTraceCollection.Name)]
public sealed class SynchronousBlockCallbackTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Transform_runs_inline_inside_processing_trace_and_restores_ambient_context()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var ambient = new Activity("caller").Start();
        var message = FlowMessage.Create((FlowValue)"input");
        var output = new BufferBlock<FlowMessage>();
        var invoked = false;
        var thread = Environment.CurrentManagedThreadId;
        IEnumerable<FlowMessage> Transform(FlowMessage value)
        {
            invoked = true;
            Environment.CurrentManagedThreadId.ShouldBe(thread);
            Activity.Current.ShouldNotBeNull().ShouldNotBeSameAs(ambient);
            return [value.WithValue((FlowValue)"result")];
        }

        var processing = message.ProcessMessageAsync(this, Transform, output);
        invoked.ShouldBeTrue();
        processing.IsCompletedSuccessfully.ShouldBeTrue();
        Activity.Current.ShouldBeSameAs(ambient);
        var result = (await processing).Single();
        result.CausationId.ShouldBe(message.MessageId);
        result.TraceId.ShouldBe(message.TraceId);
        output.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transform_and_enumeration_failures_return_only_an_error(bool duringEnumeration)
    {
        var message = FlowMessage.Create(JsonFlowValue.Null);
        var output = new BufferBlock<FlowMessage>();
        IEnumerable<FlowMessage> Transform(FlowMessage value)
            => duringEnumeration ? Broken(value) : throw new InvalidOperationException("callback failed");

        var result = (await message.ProcessMessageAsync(this, Transform, output)).Single();
        result.IsError.ShouldBeTrue();
        result.CausationId.ShouldBe(message.MessageId);
        output.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incoming_error_bypass_is_shared_by_synchronous_callbacks(bool handlesErrors)
    {
        var message = FlowMessage.Create(new FlowError("test", "incoming", "test"));
        var output = new BufferBlock<FlowMessage>();
        var transforms = 0;
        var actions = 0;
        IEnumerable<FlowMessage> Transform(FlowMessage value) { transforms++; return [value]; }
        void Process(FlowMessage _) { actions++; }

        (await message.ProcessMessageAsync(this, Transform, output, handlesErrors: handlesErrors))
            .Single().ShouldBeSameAs(message);
        (await message.ProcessMessageAsync(this, Process, output, handlesErrors: handlesErrors)).ShouldBeEmpty();
        transforms.ShouldBe(handlesErrors ? 1 : 0);
        actions.ShouldBe(handlesErrors ? 1 : 0);
        output.Count.ShouldBe(handlesErrors ? 0 : 1);
        if (!handlesErrors) output.Receive().ShouldBeSameAs(message);
    }

    [Fact]
    public async Task Synchronous_action_failure_awaits_error_admission_without_blocking_the_caller()
    {
        var output = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        var first = FlowMessage.Create((FlowValue)"first");
        var message = FlowMessage.Create((FlowValue)"second");
        await output.SendAsync(first);
        void Process(FlowMessage _) => throw new InvalidOperationException("action failed");

        var processing = message.ProcessMessageAsync(this, Process, output);
        processing.IsCompleted.ShouldBeFalse();
        (await output.ReceiveAsync(Deadline)).ShouldBeSameAs(first);
        (await processing.WaitAsync(Deadline)).ShouldBeEmpty();
        var error = await output.ReceiveAsync(Deadline);
        error.IsError.ShouldBeTrue();
        error.CausationId.ShouldBe(message.MessageId);
    }

    [Fact]
    public async Task Synchronous_flush_materializes_before_any_publication()
    {
        var output = new BufferBlock<FlowMessage>();
        var message = FlowMessage.Create(JsonFlowValue.Null);
        await Should.ThrowAsync<InvalidOperationException>(() => output.FlushMessagesAsync(() => Broken(message)).AsTask());
        output.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Synchronous_flush_still_awaits_capacity_and_preserves_order()
    {
        var output = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        var first = FlowMessage.Create((FlowValue)"first");
        var second = FlowMessage.Create((FlowValue)"second");
        var calls = 0;
        IEnumerable<FlowMessage> Flush() { calls++; return [first, second]; }

        var flushing = output.FlushMessagesAsync(Flush).AsTask();
        calls.ShouldBe(1);
        flushing.IsCompleted.ShouldBeFalse();
        (await output.ReceiveAsync(Deadline)).ShouldBeSameAs(first);
        await flushing.WaitAsync(Deadline);
        (await output.ReceiveAsync(Deadline)).ShouldBeSameAs(second);
    }

    [Fact]
    public async Task Synchronous_control_failure_preserves_token_and_faults_owner_with_original_exception()
    {
        using var stopping = new CancellationTokenSource();
        var message = FlowMessage.Create(JsonFlowValue.Null);
        var failure = new InvalidOperationException("reply failed");
        Exception? ownerFailure = null;
        void Control(FlowMessage value, CancellationToken token)
        {
            value.ShouldBeSameAs(message);
            token.ShouldBe(stopping.Token);
            throw failure;
        }

        (await Should.ThrowAsync<InvalidOperationException>(() => FlowBlockExtensions.ProcessControlAsync(
            message, this, "Reply", Control, stopping.Token, error => ownerFailure = error)))
            .ShouldBeSameAs(failure);
        ownerFailure.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Synchronous_transform_cooperative_cancellation_is_not_converted_to_error_data()
    {
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        var failure = new OperationCanceledException(stopping.Token);
        IEnumerable<FlowMessage> Transform(FlowMessage _) => throw failure;
        var message = FlowMessage.Create(JsonFlowValue.Null);
        var output = new BufferBlock<FlowMessage>();
        var processing = message.ProcessMessageAsync(this, Transform, output, stopping.Token);
        await Should.ThrowAsync<OperationCanceledException>(() => processing);
        processing.IsCanceled.ShouldBeTrue();
        output.Count.ShouldBe(0);
    }

    private static IEnumerable<FlowMessage> Broken(FlowMessage message)
    {
        yield return message;
        throw new InvalidOperationException("enumeration failed");
    }
}
