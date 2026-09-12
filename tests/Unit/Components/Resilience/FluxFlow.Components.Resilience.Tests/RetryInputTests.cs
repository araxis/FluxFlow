using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Nodes;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Resilience.Tests;

public sealed class RetryInputTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_component_uses_native_business_ports_and_preserves_message_data(bool errorFeedback)
    {
        await using var node = CreateNode(new FakeTimeProvider());
        var ports = ((IFlowComponent)node).Ports;
        ports.Controls.ShouldBeEmpty();
        ports.Inputs.Keys.ShouldBe(["Input", "Ack", "Nak", "Cancel"]);
        ports.Inputs["Input"].ShouldBeSameAs(node.Input);
        ports.Inputs["Ack"].ShouldBeSameAs(node.Ack);
        ports.Inputs["Nak"].ShouldBeSameAs(node.Nak);
        ports.Inputs["Cancel"].ShouldBeSameAs(node.Cancel);
        ports.Inputs.Values.ShouldAllBe(input => input is ActionBlock<FlowMessage>);
        ports.Outputs.ShouldHaveSingleItem().Value.ShouldBeSameAs(node.Output);
        ports.Events.ShouldBeSameAs(node.Events);
        var output = new BufferBlock<FlowMessage>();
        using var outputLink = node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var feedback = new BufferBlock<FlowMessage>();
        using var feedbackLink = feedback.LinkTo(node.Ack);
        var bytes = new byte[] { 0, 128, 255, 13, 10 };
        var input = FlowMessage.Create(FlowValue.From(new { label = "café", nested = new { count = 7 }, bytes }),
            correlationId: CorrelationId.New(), headers: new Dictionary<string, string> { ["origin"] = "caller" });

        (await node.Input.SendAsync(input)).ShouldBeTrue();
        var attempt = await output.ReceiveAsync(Deadline);
        var signal = Read(attempt);
        signal.Status.ShouldBe(RetrySignalStatus.Attempt);
        signal.Attempt.ShouldBe(1);
        signal.Reason.ShouldBe(RetryFailureReason.None);
        var value = signal.Value.ToJsonElement();
        value.GetProperty("label").GetString().ShouldBe("café");
        value.GetProperty("nested").GetProperty("count").GetInt32().ShouldBe(7);
        value.GetProperty("bytes").GetBytesFromBase64().ShouldBe(bytes);
        attempt.TraceId.ShouldBe(input.TraceId);
        attempt.CorrelationId.ShouldBe(input.CorrelationId);
        attempt.CausationId.ShouldBe(input.MessageId);
        attempt.MessageId.ShouldNotBe(input.MessageId);
        attempt.Headers["origin"].ShouldBe("caller");
        var ack = errorFeedback
            ? attempt.WithError(new FlowError("feedback", "payload is ignored", "Input"))
            : attempt.With(FlowValue.Null);
        (await feedback.SendAsync(ack)).ShouldBeTrue();
        var completed = await output.ReceiveAsync(Deadline);
        Read(completed).Status.ShouldBe(RetrySignalStatus.Completed);
        Read(completed).Value.ToString().ShouldBe(signal.Value.ToString());
        completed.CausationId.ShouldBe(ack.MessageId);
        completed.TraceId.ShouldBe(input.TraceId);
        completed.CorrelationId.ShouldBe(input.CorrelationId);
        feedback.Complete();
        await feedback.Completion.WaitAsync(Deadline);
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        await output.Completion.WaitAsync(Deadline);
        output.TryReceive(out _).ShouldBeFalse();
        ports.Inputs.Values.ShouldAllBe(input => input.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Upstream_error_is_forwarded_without_starting_an_attempt()
    {
        var clock = new FakeTimeProvider();
        await using var node = CreateNode(clock);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var error = new FlowError("upstream", "input failed", "Input");
        var input = FlowMessage.CreateError(error, correlationId: CorrelationId.New(),
            headers: new Dictionary<string, string> { ["origin"] = "failed-source" });
        (await node.Input.SendAsync(input)).ShouldBeTrue();
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeTrue();
        result.Value.ShouldBeNull();
        result.Error.ShouldBeSameAs(error);
        result.CorrelationId.ShouldBe(input.CorrelationId);
        result.TraceId.ShouldBe(input.TraceId);
        result.CausationId.ShouldBe(input.MessageId);
        result.Headers.ShouldBe(input.Headers);
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        clock.Advance(TimeSpan.FromDays(1));
        await output.Completion.WaitAsync(Deadline);
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Completion_joins_admitted_feedback_before_stopping_attempts()
    {
        using var clock = new GatedClock();
        await using var node = CreateNode(clock);
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        (await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("first")))).ShouldBeTrue();
        var attempt = await output.ReceiveAsync(Deadline);
        var ack = attempt.With(FlowValue.Null);
        (await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("second")))).ShouldBeTrue();
        var secondAttempt = await output.ReceiveAsync(Deadline);
        var secondAck = secondAttempt.With(FlowValue.Null);
        clock.Arm();
        try
        {
            (await node.Ack.SendAsync(ack)).ShouldBeTrue();
            await clock.Entered.Task.WaitAsync(Deadline);
            (await node.Ack.SendAsync(secondAck)).ShouldBeTrue();
            using var cancellation = new CancellationTokenSource();
            var blocked = node.Ack.SendAsync(ack, cancellation.Token);
            blocked.IsCompleted.ShouldBeFalse();
            cancellation.Cancel();
            var canceled = await Should.ThrowAsync<OperationCanceledException>(() => blocked.WaitAsync(Deadline));
            canceled.CancellationToken.ShouldBe(cancellation.Token);
            node.Complete();
            await node.Input.Completion.WaitAsync(Deadline);
            node.Completion.IsCompleted.ShouldBeFalse();
            node.Ack.Completion.IsCompleted.ShouldBeFalse();
            clock.Release();
            var terminal = await output.ReceiveAsync(Deadline);
            Read(terminal).Status.ShouldBe(RetrySignalStatus.Completed);
            terminal.CausationId.ShouldBe(ack.MessageId);
            Read(terminal).Value.Deserialize<string>().ShouldBe("first");
            var secondTerminal = await output.ReceiveAsync(Deadline);
            Read(secondTerminal).Status.ShouldBe(RetrySignalStatus.Completed);
            Read(secondTerminal).Value.Deserialize<string>().ShouldBe("second");
            secondTerminal.CausationId.ShouldBe(secondAck.MessageId);
            secondTerminal.TraceId.ShouldBe(secondAttempt.TraceId);
            await node.Completion.WaitAsync(Deadline);
            await output.Completion.WaitAsync(Deadline);
            output.TryReceive(out _).ShouldBeFalse();
            foreach (var input in node.Ports.Inputs.Values)
            {
                input.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                (await input.SendAsync(ack).WaitAsync(Deadline)).ShouldBeFalse();
            }
        }
        finally { clock.Release(); }
    }

    [Theory]
    [InlineData("Input")]
    [InlineData("Ack")]
    [InlineData("Nak")]
    [InlineData("Cancel")]
    [InlineData("Output")]
    public async Task Port_failure_closes_sibling_inputs_and_preserves_the_cause(string port)
    {
        await using var node = CreateNode(new FakeTimeProvider());
        var output = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var message = FlowMessage.Create(FlowValue.From("pending"));
        (await node.Input.SendAsync(message)).ShouldBeTrue();
        Read(await output.ReceiveAsync(Deadline)).Status.ShouldBe(RetrySignalStatus.Attempt);
        var failure = new InvalidOperationException("native input failed");
        IDataflowBlock origin = port == "Output" ? node.Output : node.Ports.Inputs[port];
        origin.Fault(failure);
        (await Record.ExceptionAsync(() => node.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        foreach (var input in node.Ports.Inputs.Values)
        {
            input.Completion.IsCompleted.ShouldBeTrue();
            (await input.SendAsync(message).WaitAsync(Deadline)).ShouldBeFalse();
        }
        (await Record.ExceptionAsync(() => node.Output.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        await node.Events.Completion.WaitAsync(Deadline);
        node.Events.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Direct_application_routes_feedback_as_an_ordinary_addressed_input()
    {
        await using var application = new FlowApplication();
        var node = CreateNode(new FakeTimeProvider());
        var workflow = application.AddWorkflow("main");
        var retry = workflow.Add("retry", node);
        var output = new BufferBlock<FlowMessage>();
        var observer = workflow.Add("results", new Results(output));
        retry.Output.LinkTo(observer.Input);
        observer.Output.DiscardUnmatched();
        retry.Instance.ShouldBeSameAs(node);
        application.GetInput("main.retry.Ack").ShouldBeSameAs(retry.Inputs["Ack"]);
        retry.Inputs["Ack"].IsControl.ShouldBeFalse();
        await application.StartAsync();
        (await application.SendAsync(retry.Input, FlowMessage.Create(FlowValue.From("addressed")))).ShouldBeTrue();
        var attempt = await output.ReceiveAsync(Deadline);
        Read(attempt).Status.ShouldBe(RetrySignalStatus.Attempt);
        (await application.SendAsync(retry.Inputs["Ack"], attempt.With(FlowValue.Null))).ShouldBeTrue();
        var completed = await output.ReceiveAsync(Deadline);
        Read(completed).Status.ShouldBe(RetrySignalStatus.Completed);
        Read(completed).Value.Deserialize<string>().ShouldBe("addressed");
        await application.StopAsync().WaitAsync(Deadline);
        await application.Completion.WaitAsync(Deadline);
        node.Ports.Inputs.Values.ShouldAllBe(input => input.Completion.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData("Ack")]
    [InlineData("Nak")]
    [InlineData("Cancel")]
    public async Task Feedback_inputs_do_not_bypass_cycle_validation(string port)
    {
        await using var application = new FlowApplication();
        var node = CreateNode(new FakeTimeProvider());
        var retry = application.AddWorkflow("main").Add("retry", node);
        retry.Output.LinkTo(retry.Inputs[port]);
        retry.Inputs[port].IsControl.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => application.Validate()).Message
            .ShouldBe("The application contains a cycle. Feedback execution is not supported.");
        await application.StopAsync().WaitAsync(Deadline);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private sealed class Results(BufferBlock<FlowMessage> output) : FlowNode
    {
        protected override Task ProcessAsync(FlowMessage message) => output.SendAsync(message);
    }

    private static FlowRetryNode CreateNode(TimeProvider clock)
        => new(new FlowRetryOptions { Capacity = 2, AttemptTimeoutMilliseconds = 30_000 }, clock);

    private static RetrySignal<FlowValue> Read(FlowMessage message)
        => message.Value.ShouldNotBeNull().Deserialize<RetrySignal<FlowValue>>().ShouldNotBeNull();

    private sealed class GatedClock : TimeProvider, IDisposable
    {
        private readonly FakeTimeProvider _inner = new();
        private readonly ManualResetEventSlim _release = new();
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public void Release() => _release.Set();
        public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new GatedTimer(_inner.CreateTimer(callback, state, dueTime, period), this);
        public void Dispose() { Release(); _release.Dispose(); }
        private sealed class GatedTimer(ITimer inner, GatedClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Interlocked.Exchange(ref owner._armed, 0) == 1)
                {
                    owner.Entered.TrySetResult();
                    if (!owner._release.Wait(Deadline)) throw new TimeoutException("Feedback gate was not released.");
                }
                return inner.Change(dueTime, period);
            }
            public void Dispose() => inner.Dispose();
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
