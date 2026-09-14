using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class CallFunctionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Nested_concurrent_calls_keep_each_outer_exchange_and_payload()
    {
        await using var app = new FlowApplication();
        var first = app.AddWorkflow("first");
        var second = app.AddWorkflow("second");
        first.Add("call", new CallFunction("second.call.Input", "second.call.Output", Deadline,
            new() { MaxDegreeOfParallelism = 4 }));
        second.Add("call", new CallFunction("target.Input", "target.Output", Deadline,
            new() { MaxDegreeOfParallelism = 4 }));
        second.Add("target", new Process(async message =>
        {
            var value = message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await Task.Delay(value % 3);
            return message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value * 2, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }));
        await app.StartAsync();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async value =>
        {
            var result = await app.RequestAsync("first.call.Input", "first.call.Output",
                FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Deadline);
            result.IsError.ShouldBeFalse();
            result.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(value * 2);
        }));
        app.PendingRequestCount.ShouldBe(0);
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Fact]
    public async Task Timeout_and_target_exception_become_errors_and_later_requests_still_work()
    {
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("w");
        workflow.Add("call", new CallFunction("target.Input", "target.Output", TimeSpan.FromMilliseconds(100)));
        workflow.Add("target", new Process(async message =>
        {
            var value = message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (value == 0) await Task.Delay(300);
            if (value == 1) throw new InvalidOperationException("target failed");
            return message;
        })).Output.DiscardUnmatched();
        await app.StartAsync();
        async Task<FlowMessage> Call(int value) => await app.RequestAsync("w.call.Input", "w.call.Output",
            FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Deadline);
        (await Call(0)).IsError.ShouldBeTrue();
        await Task.Delay(350);
        var failure = await Call(1);
        failure.IsError.ShouldBeTrue();
        failure.Error!.Message.ShouldBe("target failed");
        (await Call(2)).Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(2);
        app.PendingRequestCount.ShouldBe(0);
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Fact]
    public async Task Target_stays_open_after_ordinary_producer_finishes_while_caller_is_alive()
    {
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("w");
        var producer = new Process(Task.FromResult);
        var from = workflow.Add("producer", producer);
        var target = workflow.Add("target", new Process(Task.FromResult));
        workflow.Add("call", new CallFunction("target.Input", "target.Output", Deadline));
        from.Output.LinkTo(target.Input);
        await app.StartAsync();
        producer.Complete();
        await producer.Completion.WaitAsync(Deadline);
        var result = await app.RequestAsync("w.call.Input", "w.call.Output",
            FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(7, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Deadline);
        result.IsError.ShouldBeFalse();
        result.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(7);
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Fact]
    public async Task Incoming_error_bypasses_target_and_preserves_original_correlation()
    {
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("w");
        var calls = 0;
        workflow.Add("call", new CallFunction("target.Input", "target.Output", Deadline));
        workflow.Add("target", new Process(message => { calls++; return Task.FromResult(message); }));
        await app.StartAsync();
        var error = new FlowError("test", "original failure", "Test");
        var result = await app.RequestAsync("w.call.Input", "w.call.Output",
            FlowMessage.Create(JsonFlowValue.Null).WithError(error), Deadline);
        result.Error.ShouldBe(error);
        calls.ShouldBe(0);
    }

    [Fact]
    public async Task Stop_releases_pending_internal_request_without_waiting_for_infinite_timeout()
    {
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("w");
        workflow.Add("call", new CallFunction("target.Input", "unused.Output", Timeout.InfiniteTimeSpan)).Output.DiscardUnmatched();
        workflow.Add("target", new Process(Task.FromResult)).Output.DiscardUnmatched();
        workflow.Add("unused", new Process(Task.FromResult));
        await app.StartAsync();
        await app.GetInput("w.call.Input").Block.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        using var deadline = new CancellationTokenSource(Deadline);
        while (app.PendingRequestCount == 0) await Task.Delay(1, deadline.Token);
        await app.StopAsync().WaitAsync(Deadline);
        app.PendingRequestCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("missing.Input", "target.Output")]
    [InlineData("target.Output", "target.Output")]
    [InlineData("target.Input", "target.Input")]
    public async Task Invalid_addresses_are_rejected_before_start(string request, string reply)
    {
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("w");
        workflow.Add("call", new CallFunction(request, reply, Deadline));
        workflow.Add("target", new Process(Task.FromResult));
        Should.Throw<KeyNotFoundException>(app.Validate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Self_and_indirect_call_cycles_are_rejected(bool indirect)
    {
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("w");
        workflow.Add("call", new CallFunction(indirect ? "other.Input" : "call.Input",
            indirect ? "other.Output" : "call.Output", Deadline));
        if (indirect) workflow.Add("other", new CallFunction("call.Input", "call.Output", Deadline));
        Should.Throw<InvalidOperationException>(app.Validate).Message.ShouldContain("cycle");
    }

    private sealed class Process(Func<FlowMessage, Task<FlowMessage>> run) : FlowTransformNode
    {
        protected override Task<FlowMessage> TransformAsync(FlowMessage message) => run(message);
    }
}
