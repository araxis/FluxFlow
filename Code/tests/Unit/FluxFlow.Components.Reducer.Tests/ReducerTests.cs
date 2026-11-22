using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Reducer.Tests;

public sealed class ReducerTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static FlowValue Number(int number) => new JsonFlowValue(JsonSerializer.SerializeToElement(number));

    [Fact]
    public async Task Equal_candidate_is_not_stored_or_emitted_and_changes_keep_lineage()
    {
        var initial = Number(2);
        var states = new List<FlowValue>();
        await using var reducer = new Reducer(new()
        {
            InitialState = initial,
            Reduce = (state, input) => { states.Add(state); return Number(state.Json.GetInt32() + input.Json.GetInt32()); },
            AreEqual = (a, b) => a.Json.GetInt32() == b.Json.GetInt32()
        });
        var sink = Sink(reducer);
        var first = FlowMessage.Create(Number(0));
        var changed = FlowMessage.Create(Number(3), CorrelationId.New());
        await reducer.Input.SendAsync(first);
        await reducer.Input.SendAsync(changed);
        await reducer.Input.SendAsync(FlowMessage.Create(Number(0)));
        reducer.Complete();
        var result = await sink.ReceiveAsync(Deadline);
        await reducer.Completion.WaitAsync(Deadline);
        result.Value.Json.GetInt32().ShouldBe(5);
        result.CorrelationId.ShouldBe(changed.CorrelationId);
        result.TraceId.ShouldBe(changed.TraceId);
        result.CausationId.ShouldBe(changed.MessageId);
        states[0].ShouldBeSameAs(initial);
        states[1].ShouldBeSameAs(initial);
        states[2].ShouldBeSameAs(result.Value);
        sink.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reduction_or_comparison_failure_preserves_state_and_next_input_runs(bool comparerFails)
    {
        var failed = false;
        await using var reducer = new Reducer(new()
        {
            InitialState = Number(2),
            Reduce = (state, input) =>
            {
                if (!comparerFails && !failed) { failed = true; throw new InvalidOperationException("reduce"); }
                return Number(state.Json.GetInt32() + input.Json.GetInt32());
            },
            AreEqual = (a, b) =>
            {
                if (comparerFails && !failed) { failed = true; throw new InvalidOperationException("compare"); }
                return a.Json.GetInt32() == b.Json.GetInt32();
            }
        });
        var sink = Sink(reducer);
        await reducer.Input.SendAsync(FlowMessage.Create(Number(100)));
        await reducer.Input.SendAsync(FlowMessage.Create(Number(1)));
        reducer.Complete();
        (await sink.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        (await sink.ReceiveAsync(Deadline)).Value.Json.GetInt32().ShouldBe(3);
        await reducer.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Incoming_errors_bypass_and_each_instance_has_its_own_state()
    {
        var options = new ReducerOptions
        {
            InitialState = Number(0), Reduce = (a, b) => Number(a.Json.GetInt32() + b.Json.GetInt32()),
            AreEqual = (a, b) => a.Json.GetInt32() == b.Json.GetInt32()
        };
        await using var one = new Reducer(options);
        await using var two = new Reducer(options);
        var first = Sink(one);
        var second = Sink(two);
        var error = FlowMessage.Create((FlowValue)new FlowError("test", "test", "test"));
        await one.Input.SendAsync(error);
        await one.Input.SendAsync(FlowMessage.Create(Number(2)));
        await two.Input.SendAsync(FlowMessage.Create(Number(1)));
        one.Complete(); two.Complete();
        (await first.ReceiveAsync(Deadline)).ShouldBeSameAs(error);
        (await first.ReceiveAsync(Deadline)).Value.Json.GetInt32().ShouldBe(2);
        (await second.ReceiveAsync(Deadline)).Value.Json.GetInt32().ShouldBe(1);
        await Task.WhenAll(one.Completion, two.Completion).WaitAsync(Deadline);
    }

    [Fact]
    public void Parallel_processing_is_rejected_without_mutating_options()
    {
        var execution = new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 2 };
        Should.Throw<ArgumentException>(() => new Reducer(new()
        { InitialState = Number(0), Reduce = (a, b) => b, AreEqual = ReferenceEquals, Execution = execution }));
        execution.MaxDegreeOfParallelism.ShouldBe(2);
    }

    private static BufferBlock<FlowMessage> Sink(Reducer reducer)
    {
        var sink = new BufferBlock<FlowMessage>();
        reducer.Output.LinkTo(sink, new() { PropagateCompletion = true });
        return sink;
    }
}
