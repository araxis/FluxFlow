using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Core;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sources.Tests;

public sealed class SourcesCoreGraphTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sources_are_direct_Core_components_with_actual_ports_and_reference_fanout(bool generated)
    {
        FlowSource source = generated
            ? new GeneratedSourceNode<int>(new GeneratedSourceOptions { BoundedCapacity = 2 }, [3, 5, 7])
            : new SequenceSourceNode(new SequenceSourceOptions { Start = 3, Step = 2, Count = 3, BoundedCapacity = 2 });
        var first = new Sink();
        var second = new Sink();
        await using var app = new FlowApplication(linkCapacity: 2);
        var main = app.AddWorkflow("main");
        var component = main.Add("source", source);
        component.Instance.ShouldBeSameAs(source);
        source.Ports.Inputs.ShouldBeEmpty();
        source.Ports.Outputs.Keys.ShouldBe(["Output"]);
        source.Ports.Outputs["Output"].ShouldBeSameAs(source.Output);
        source.Ports.Events.ShouldBeSameAs(source.Events);
        component.Output.LinkTo(main.Add("first", first).Input);
        component.Output.LinkTo(app.AddWorkflow("reports").Add("second", second).Input);

        await app.StartAsync();
        await app.Completion.WaitAsync(Deadline);

        first.Messages.Select(message => generated ? message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) :
            message.Value!.Json.Deserialize<SequenceItem>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Value).ShouldBe([3L, 5L, 7L]);
        first.Messages.Count.ShouldBe(3);
        second.Messages.Count.ShouldBe(3);
        for (var index = 0; index < 3; index++)
            first.Messages.ElementAt(index).ShouldBeSameAs(second.Messages.ElementAt(index));
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await app.DisposeAsync();
        first.DisposeCalls.ShouldBe(1);
        second.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_source_stop_cancels_timing_and_drains(bool generated)
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        FlowSource source = generated
            ? new GeneratedSourceNode<int>(new GeneratedSourceOptions
            { InitialDelayMilliseconds = 10, IntervalMilliseconds = 20, Loop = true, MaxItems = 5, BoundedCapacity = 2 }, [3, 5], clock)
            : new SequenceSourceNode(new SequenceSourceOptions
            { InitialDelayMilliseconds = 10, IntervalMilliseconds = 20, Count = 5, Start = 3, BoundedCapacity = 2 }, clock);
        await using var app = new FlowApplication();
        var sink = new Sink();
        var main = app.AddWorkflow("main");
        main.Add("source", source).Output.LinkTo(main.Add("sink", sink).Input);
        var initialTimer = clock.TimerScheduled;

        await app.StartAsync();
        await initialTimer.WaitAsync(Deadline);
        sink.Messages.ShouldBeEmpty();
        var intervalTimer = clock.TimerScheduled;
        clock.Advance(TimeSpan.FromMilliseconds(10));
        await intervalTimer.WaitAsync(Deadline);
        await app.StopAsync().WaitAsync(Deadline);

        sink.Messages.Count.ShouldBe(1);
        clock.CreatedTimerCount.ShouldBe(2);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        clock.Advance(TimeSpan.FromDays(1));
        sink.Messages.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("[1,2]", "1|2")]
    [InlineData("{\"name\":\"item\"}", "{\"name\":\"item\"}")]
    [InlineData("null", "null")]
    [InlineData("true", "true")]
    [InlineData("[]", "")]
    public async Task Generated_factory_detaches_json_items_before_document_disposal(string json, string expected)
    {
        GeneratedSourceNode source;
        using (var document = JsonDocument.Parse(json))
            source = GeneratedSourceNode.Create(new GeneratedSourceOptions { BoundedCapacity = 2 }, document.RootElement);
        await using var app = new FlowApplication();
        var sink = new Sink();
        var main = app.AddWorkflow("main");
        main.Add("source", source).Output.LinkTo(main.Add("sink", sink).Input);

        await app.StartAsync();
        await app.Completion.WaitAsync(Deadline);

        string.Join('|', sink.Messages.Select(message => message.Value!.Json.GetRawText()))
            .ShouldBe(expected);
        sink.Messages.Count.ShouldBe(json == "[]" ? 0 : json == "[1,2]" ? 2 : 1);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private sealed class Sink : IFlowComponent
    {
        private readonly ActionBlock<FlowMessage> _input;
        public Sink()
        {
            _input = new ActionBlock<FlowMessage>(Messages.Enqueue,
                new ExecutionDataflowBlockOptions { BoundedCapacity = 2 });
            Ports = new FlowPorts(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });
        }
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public FlowPorts Ports { get; }
        public int DisposeCalls { get; private set; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            Complete();
            await Completion;
        }
    }
}
