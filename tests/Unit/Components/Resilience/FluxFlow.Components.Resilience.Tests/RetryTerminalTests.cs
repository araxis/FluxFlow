using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Nodes;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Nodes;
using FluxFlow.Data;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Resilience.Tests;

public sealed class RetryTerminalTests
{
    private static RetrySignal<string> Read(FlowMessage message)
        => message.Value.ShouldNotBeNull().Json.Deserialize<RetrySignal<string>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull();

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("admission", false)]
    [InlineData("admission", true)]
    [InlineData("timer", false)]
    [InlineData("timer", true)]
    [InlineData("feedback", false)]
    [InlineData("feedback", true)]
    [InlineData("stop", false)]
    [InlineData("stop", true)]
    [InlineData("fault", false)]
    [InlineData("fault", true)]
    public async Task Retry_timer_failure_completes_and_closes_all_ports(string operation, bool disposalFails)
    {
        var clock = new FailureClock { DisposalFails = disposalFails };
        var node = CreateNode(clock);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var first = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("first", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        (await node.Input.SendAsync(first)).ShouldBeTrue();
        var attempt = await output.ReceiveAsync().WaitAsync(Deadline);
        attempt.TraceId.ShouldBe(first.TraceId);
        Read(attempt).Value.ShouldBe("first");
        clock.Inner.Advance(TimeSpan.FromMilliseconds(500));
        (await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("second", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        Read(await output.ReceiveAsync().WaitAsync(Deadline)).Value.ShouldBe("second");
        var initiatingError = new InvalidOperationException("input failed");
        clock.ChangeFails = true;
        if (operation == "admission")
            (await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("third", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        else if (operation == "timer")
            clock.Inner.Advance(TimeSpan.FromMilliseconds(500));
        else if (operation == "feedback")
            (await node.Ack.SendAsync(attempt.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("ack", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        else if (operation == "fault") node.Fault(initiatingError);
        else node.Complete();

        var error = await Record.ExceptionAsync(() => node.Completion.WaitAsync(Deadline));
        error.ShouldNotBeNull();
        error.ShouldNotBeOfType<TimeoutException>();
        var failures = Flatten(error);
        failures.ShouldContain(value => ReferenceEquals(value, clock.ChangeFailure));
        failures.Length.ShouldBe(operation == "fault" ? 2 : 1);
        if (operation == "fault") failures.ShouldContain(value => ReferenceEquals(value, initiatingError));
        node.Ack.Completion.IsCompleted.ShouldBeTrue();
        node.Nak.Completion.IsCompleted.ShouldBeTrue();
        node.Cancel.Completion.IsCompleted.ShouldBeTrue();
        (await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("later", new JsonSerializerOptions(JsonSerializerDefaults.Web))))).WaitAsync(Deadline)).ShouldBeFalse();
        (await node.Ack.SendAsync(attempt.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("late", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeFalse();
        var inputError = await Record.ExceptionAsync(() => node.Input.Completion.WaitAsync(Deadline));
        inputError.ShouldNotBeOfType<TimeoutException>();
        node.Input.Completion.IsCompleted.ShouldBeTrue();
        var outputError = await Record.ExceptionAsync(() => node.Output.Completion.WaitAsync(Deadline));
        outputError.ShouldNotBeNull();
        outputError.ShouldNotBeOfType<TimeoutException>();
        await node.Events.Completion.WaitAsync(Deadline);

        var disposalError = await Record.ExceptionAsync(() => node.DisposeAsync().AsTask().WaitAsync(Deadline));
        var expected = new[]
        {
            operation is "stop" or "fault" ? null : clock.ChangeFailure,
            disposalFails ? clock.DisposalFailure : null
        }.OfType<Exception>().ToArray();
        if (expected.Length == 0) disposalError.ShouldBeNull();
        else
        {
            disposalError.ShouldNotBeNull();
            Flatten(disposalError).ShouldBe(expected);
        }
        await node.DisposeAsync();
        clock.AsyncDisposals.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_shutdown_cancels_backoff_before_timer_cleanup(bool disposalFails)
    {
        var clock = new FailureClock { DisposalFails = disposalFails };
        var node = CreateNode(clock);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("waiting", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        (await node.Input.SendAsync(input)).ShouldBeTrue();
        var attempt = await output.ReceiveAsync().WaitAsync(Deadline);
        (await node.Nak.SendAsync(attempt.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("nak", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
        var scheduled = await output.ReceiveAsync().WaitAsync(Deadline);
        scheduled.Error!.Code.ShouldBe(RetryErrorCodeNames.Nak);
        clock.TimersCreated.ShouldBe(2);
        var before = clock.GetUtcNow();

        node.Complete();
        var terminal = await output.ReceiveAsync().WaitAsync(Deadline);
        terminal.Error!.Code.ShouldBe(RetryErrorCodeNames.Stopped);
        terminal.TraceId.ShouldBe(input.TraceId);
        await node.Completion.WaitAsync(Deadline);
        await output.Completion.WaitAsync(Deadline);
        clock.GetUtcNow().ShouldBe(before);
        clock.SyncDisposals.ShouldBe(1);
        output.TryReceive(out _).ShouldBeFalse();
        (await node.Cancel.SendAsync(attempt.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("late", new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeFalse();
        if (disposalFails)
            (await Should.ThrowAsync<InvalidOperationException>(() => node.DisposeAsync().AsTask().WaitAsync(Deadline)))
                .ShouldBeSameAs(clock.DisposalFailure);
        else await node.DisposeAsync();
        await node.DisposeAsync();
        clock.AsyncDisposals.ShouldBe(1);
        clock.SyncDisposals.ShouldBe(1);
    }

    private static Exception[] Flatten(Exception error)
        => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray() : [error];

    private static FlowRetryNode CreateNode(TimeProvider clock)
        => new(new FlowRetryOptions
        {
            Capacity = 4,
            MaximumAttempts = 3,
            AttemptTimeoutMilliseconds = 1_000,
            InitialDelayMilliseconds = 30_000,
            MaximumDelayMilliseconds = 30_000,
            JitterFactor = 0
        }, clock);

    private sealed class FailureClock : TimeProvider
    {
        private int _created;
        private int _syncDisposals;
        private int _asyncDisposals;
        public FakeTimeProvider Inner { get; } = new();
        public bool ChangeFails { get; set; }
        public bool DisposalFails { get; set; }
        public InvalidOperationException ChangeFailure { get; } = new("timer change failed");
        public InvalidOperationException DisposalFailure { get; } = new("timer disposal failed");
        public int TimersCreated => Volatile.Read(ref _created);
        public int SyncDisposals => Volatile.Read(ref _syncDisposals);
        public int AsyncDisposals => Volatile.Read(ref _asyncDisposals);
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _created);
            return new Timer(Inner.CreateTimer(callback, state, dueTime, period), this);
        }
        private sealed class Timer(ITimer inner, FailureClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
                => owner.ChangeFails ? throw owner.ChangeFailure : inner.Change(dueTime, period);
            public void Dispose()
            {
                Interlocked.Increment(ref owner._syncDisposals);
                inner.Dispose();
            }
            public async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._asyncDisposals);
                await inner.DisposeAsync();
                if (owner.DisposalFails) throw owner.DisposalFailure;
            }
        }
    }
}
