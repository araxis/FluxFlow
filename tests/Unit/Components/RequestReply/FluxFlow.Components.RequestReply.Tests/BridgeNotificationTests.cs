using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.RequestReply.Tests;

public sealed class BridgeNotificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Failed_reply_callback_is_not_retried_and_does_not_stop_other_callers()
    {
        await using var bridge = new RequestReplyCoordinator<string, string>(new RequestReplyOptions { Capacity = 2 });
        var output = new BufferBlock<FlowMessage>();
        using var outputLink = bridge.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });
        var reported = new TaskCompletionSource<FlowEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new ActionBlock<FlowMessage>(message =>
        {
            var diagnostic = message.ToFlowEvent();
            if (diagnostic.Attributes.TryGetValue("code", out var code)
                && Convert.ToInt32(code) == RequestReplyErrorCodes.ReplyFailed) reported.TrySetResult(diagnostic);
        });
        using var eventLink = bridge.Events.LinkTo(observer, new DataflowLinkOptions { PropagateCompletion = true });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("caller disconnected");
        var first = new Context("first", release.Task, failure);
        var second = new Context("second", Task.CompletedTask);
        await bridge.StartAsync();
        CorrelationId? firstExchange = null;
        try
        {
            (await bridge.Incoming.SendAsync(first)).ShouldBeTrue();
            var firstRequest = await output.ReceiveAsync().WaitAsync(Deadline);
            firstExchange = firstRequest.CorrelationId;
            (await bridge.Responses.SendAsync(firstRequest.With(FlowValue.From("first reply")))).ShouldBeTrue();
            await first.Entered.Task.WaitAsync(Deadline);
            (await bridge.Incoming.SendAsync(second)).ShouldBeTrue();
            var secondRequest = await output.ReceiveAsync().WaitAsync(Deadline);
            secondRequest.Value!.Deserialize<string>().ShouldBe("second");
            (await bridge.Responses.SendAsync(secondRequest.With(FlowValue.From("second reply")))).ShouldBeTrue();
            await second.Exited.Task.WaitAsync(Deadline);
            first.Exited.Task.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        var diagnostic = await reported.Task.WaitAsync(Deadline);
        diagnostic.CorrelationId.ShouldBe(firstExchange);
        diagnostic.Level.ShouldBe(FlowEventLevel.Error);
        diagnostic.Message.ShouldBe("Failed to reply: caller disconnected");
        bridge.Complete();
        await bridge.Completion.WaitAsync(Deadline);
        await observer.Completion.WaitAsync(Deadline);
        foreach (var context in new[] { first, second })
        {
            context.Replies.ShouldBe(1);
            context.Failures.ShouldBe(0);
            context.Acknowledgements.ShouldBe(0);
            context.Token.CanBeCanceled.ShouldBeTrue();
        }
        first.Value.ShouldBe("first reply");
        second.Value.ShouldBe("second reply");
        bridge.InFlightCount.ShouldBe(0);
    }

    [Fact]
    public void Options_serialize_only_effective_settings_and_validate_record_updates()
    {
        var options = new RequestReplyOptions();
        var json = JsonSerializer.SerializeToElement(options);
        json.EnumerateObject().Select(property => property.Name).ShouldBe(["Mode", "Timeout", "Capacity"]);
        json.GetProperty("Mode").GetInt32().ShouldBe((int)RequestReplyMode.RequestReply);
        json.GetProperty("Timeout").GetString().ShouldBe("00:00:30");
        json.GetProperty("Capacity").GetInt32().ShouldBe(128);
        Should.Throw<ArgumentOutOfRangeException>(() => options with { Timeout = TimeSpan.Zero })
            .ParamName.ShouldBe(nameof(RequestReplyOptions.Timeout));
        Should.Throw<ArgumentOutOfRangeException>(() => options with { Capacity = 0 })
            .ActualValue.ShouldBe(0);
        Should.Throw<ArgumentOutOfRangeException>(() => options with { Mode = (RequestReplyMode)99 })
            .ActualValue.ShouldBe((RequestReplyMode)99);
        var configured = options with { Mode = RequestReplyMode.FireAndForget, Capacity = 2, Timeout = TimeSpan.FromSeconds(1) };
        configured.Mode.ShouldBe(RequestReplyMode.FireAndForget);
        configured.Capacity.ShouldBe(2);
        configured.Timeout.ShouldBe(TimeSpan.FromSeconds(1));
        options.Capacity.ShouldBe(128);
    }

    private sealed class Context(string request, Task release, Exception? failure = null) : IRequestContext<string, string>
    {
        public string Request => request;
        public CorrelationId? CorrelationId => null;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Replies { get; private set; }
        public int Failures { get; private set; }
        public int Acknowledgements { get; private set; }
        public string? Value { get; private set; }
        public CancellationToken Token { get; private set; }
        public async Task ReplyAsync(string value, CancellationToken cancellationToken = default)
        {
            Replies++;
            Value = value;
            Token = cancellationToken;
            Entered.TrySetResult();
            await release;
            Exited.TrySetResult();
            if (failure is not null) throw failure;
        }
        public Task FailAsync(Exception error, CancellationToken cancellationToken = default)
        { Failures++; return Task.CompletedTask; }
        public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
        { Acknowledgements++; return Task.CompletedTask; }
    }
}
