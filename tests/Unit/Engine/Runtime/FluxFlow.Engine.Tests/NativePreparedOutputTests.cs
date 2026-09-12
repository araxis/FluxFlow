using System.Collections.Concurrent;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class NativePreparedOutputTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Address =
        ApplicationAddress.WorkflowPort("Main", "Source", "Output");

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Prepared_native_output_retains_order_until_activation(int capacity)
    {
        var capture = new RecordingCapture();
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var output = CreateOutput(capacity, capture, rejections);
        await using var source = new FlowOutput<FlowMessage>(new() { Capacity = capacity });
        using var prepared = output.PrepareRevisionOutput(source);
        var messages = Messages(capacity);
        try
        {
            foreach (var message in messages)
                (await source.SendAsync(message).AsTask().WaitAsync(Deadline)).ShouldBeTrue();

            // This barrier proves native delivery has finished, not merely that
            // producer admission returned before another scheduler had a turn.
            await source.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            capture.Messages.ShouldBeEmpty();
            output.GetStatus().ActiveAttachments.ShouldBe(0);

            prepared.Activate();
            source.Complete();
            await prepared.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            var delivered = capture.Messages.ToArray();
            delivered.Length.ShouldBe(messages.Length);
            for (var index = 0; index < messages.Length; index++)
                delivered[index].ShouldBeSameAs(messages[index]);
            output.GetStatus().PendingMessages.ShouldBe(0);
            rejections.ShouldBeEmpty();
        }
        finally
        {
            prepared.Dispose();
            output.Abort();
            await output.Completion.WaitAsync(Deadline);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Disposed_prepared_output_does_not_leak_into_replacement(int capacity)
    {
        var capture = new RecordingCapture();
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var output = CreateOutput(capacity, capture, rejections);
        await using var source = new FlowOutput<FlowMessage>(new() { Capacity = capacity });
        await using var replacement = new FlowOutput<FlowMessage>(new() { Capacity = capacity });
        using var abandoned = output.PrepareRevisionOutput(source);
        try
        {
            foreach (var message in Messages(capacity))
                (await source.SendAsync(message).AsTask().WaitAsync(Deadline)).ShouldBeTrue();
            await source.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);

            abandoned.Dispose();
            abandoned.Dispose();
            Should.Throw<ObjectDisposedException>(() => abandoned.Activate());
            source.Complete();
            await source.Completion.WaitAsync(Deadline);
            capture.Messages.ShouldBeEmpty();
            output.GetStatus().ActiveAttachments.ShouldBe(0);
            output.Completion.IsCompleted.ShouldBeFalse();

            using var prepared = output.PrepareRevisionOutput(replacement);
            prepared.Activate();
            var expected = FlowMessage.Create(FlowValue.From("replacement"));
            (await replacement.SendAsync(expected).AsTask().WaitAsync(Deadline)).ShouldBeTrue();
            replacement.Complete();
            await prepared.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            capture.Messages.Single().ShouldBeSameAs(expected);
            output.GetStatus().PendingMessages.ShouldBe(0);
            rejections.ShouldBeEmpty();
        }
        finally
        {
            abandoned.Dispose();
            output.Abort();
            await output.Completion.WaitAsync(Deadline);
        }
    }

    private static FlowMessage[] Messages(int count)
        => Enumerable.Range(0, count).Select(value => FlowMessage.Create(FlowValue.From(value))).ToArray();

    private static ApplicationOutputPort CreateOutput(int capacity, RecordingCapture capture,
        ConcurrentQueue<ApplicationPortRejection> rejections)
        => new(Address, capacity, rejections.Enqueue, static _ => { },
            new ApplicationRevisionRouting(), capture, discardUnmatched: true);

    private sealed class RecordingCapture : IApplicationOutputCapture
    {
        internal ConcurrentQueue<FlowMessage> Messages { get; } = new();

        public ValueTask CaptureAsync(FlowMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Enqueue(message);
            return ValueTask.CompletedTask;
        }
    }
}
