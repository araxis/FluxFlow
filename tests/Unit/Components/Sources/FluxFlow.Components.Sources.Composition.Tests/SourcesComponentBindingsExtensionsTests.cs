using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Core;
using FluxFlow.Json;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sources.Composition.Tests;

public sealed class SourcesComponentBindingsExtensionsTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Document_sources_use_direct_nodes_and_preserve_options(bool generated)
    {
        var clock = new BorrowedClock();
        var sink = new Sink();
        var bindings = new ComponentBindings();
        bindings.AddSources(clock).ShouldBeSameAs(bindings);
        bindings.Add("test.sink", _ => sink);
        var options = generated
            ? "\"items\":[2,4],\"loop\":true,\"maxItems\":5"
            : "\"start\":2,\"step\":2,\"count\":5,\"name\":\"numbers\"";
        await using var app = await FlowJson.BuildAsync(Document(generated, options), bindings);
        sink.Messages.ShouldBeEmpty();

        await app.StartAsync();
        await app.Completion.WaitAsync(Deadline);

        sink.Messages.Select(message => generated ? message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) :
            message.Value!.Json.Deserialize<SequenceItem>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Value).ShouldBe(generated ? [2L, 4L, 2L, 4L, 2L] : [2L, 4L, 6L, 8L, 10L]);
        if (!generated)
        {
            sink.Messages.ShouldAllBe(message => message.Value!.Json.Deserialize<SequenceItem>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Name == "numbers");
            sink.Messages.ShouldAllBe(message => message.Value!.Json.Deserialize<SequenceItem>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Timestamp == clock.GetUtcNow());
        }
        await app.DisposeAsync();
        sink.DisposeCalls.ShouldBe(1);
        clock.DisposeCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Document_bindings_resolve_explicit_clocks_and_preserve_borrowed_ownership()
    {
        var defaultClock = new BorrowedClock();
        var namedClock = new BorrowedClock();
        namedClock.Advance(TimeSpan.FromDays(3));
        var keys = new List<string>();
        var sink = new Sink();
        var bindings = new ComponentBindings().AddSources(defaultClock, key => { keys.Add(key); return namedClock; });
        bindings.Add("test.sink", _ => sink);
        await using var app = await FlowJson.BuildAsync(Document(false, "\"clock\":\" Resources.clock \",\"count\":2"), bindings);

        await app.StartAsync();
        await app.Completion.WaitAsync(Deadline);
        await app.DisposeAsync();

        keys.ShouldBe(["Resources.clock"]);
        sink.Messages.Count.ShouldBe(2);
        sink.Messages.ShouldAllBe(message => message.Value!.Json.Deserialize<SequenceItem>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Timestamp == namedClock.GetUtcNow());
        defaultClock.DisposeCalls.ShouldBe(0);
        namedClock.DisposeCalls.ShouldBe(0);
        sink.DisposeCalls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, "\"clock\":\"missing\"", "InvalidOperationException")]
    [InlineData(true, "\"clock\":\"missing\"", "InvalidOperationException")]
    [InlineData(false, "\"clock\":42", "JsonException")]
    [InlineData(true, "\"clock\":\" \"", "JsonException")]
    [InlineData(false, "\"processing\":\"Resources.parallel\"", "NotSupportedException")]
    [InlineData(true, "\"Processing\":\"Resources.parallel\"", "NotSupportedException")]
    public async Task Document_bindings_reject_unresolved_or_unsupported_configuration(bool generated, string options, string exceptionType)
    {
        var clock = new BorrowedClock();
        var resolutions = 0;
        var bindings = new ComponentBindings().AddSources(clock, _ => { resolutions++; return null; });
        bindings.Add("test.sink", _ => new Sink());

        var error = await Should.ThrowAsync<Exception>(() => FlowJson.BuildAsync(Document(generated, options), bindings));

        error.GetType().Name.ShouldBe(exceptionType);
        resolutions.ShouldBe(exceptionType == "InvalidOperationException" ? 1 : 0);
        clock.DisposeCalls.ShouldBe(0);
        error.Message.ShouldContain(exceptionType == "NotSupportedException" ? "processing-profile" : "clock");
    }

    [Theory]
    [InlineData(false, "\"count\":0")]
    [InlineData(true, "\"maxItems\":0")]
    public async Task Document_constructor_failure_disposes_previously_owned_components(bool generated, string options)
    {
        var sink = new Sink();
        var bindings = new ComponentBindings().AddSources();
        bindings.Add("test.sink", _ => sink);
        var type = generated ? "source.items" : "source.sequence";
        var json = "{\"Resources\":{},\"Workflows\":{\"main\":{\"first\":{\"Type\":\"test.sink\"},\"invalid\":{\"Type\":\"" + type + "\"," + options + "}}}}";
        var parsed = JsonSerializer.Deserialize<ApplicationDefinition>(json)!;
        var components = parsed.Workflows["main"].Components;
        var names = components.Keys.ToArray();
        // Document maps do not promise textual order. Establish the failure after an owned instance.
        var workflow = new WorkflowDefinition([
            new(names[0], components["first"]), new(names[1], components["invalid"])]);
        workflow.Components.Values.First().ShouldBeSameAs(components["first"]);
        var definition = new ApplicationDefinition(workflows: [new("main", workflow)]);

        var error = await Should.ThrowAsync<ArgumentException>(() => FlowJson.BuildAsync(definition, bindings));

        error.ParamName.ShouldBe("options");
        error.Message.ShouldContain(generated ? "maxItems" : "count");
        sink.DisposeCalls.ShouldBe(1);
        sink.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        sink.Messages.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Document_defaults_match_standalone_source_defaults(bool generated)
    {
        var sink = new Sink();
        var bindings = new ComponentBindings().AddSources();
        bindings.Add("test.sink", _ => sink);
        await using var app = await FlowJson.BuildAsync(Document(generated, ""), bindings);
        await app.StartAsync();
        await app.Completion.WaitAsync(Deadline);
        sink.Messages.Count.ShouldBe(generated ? 0 : 1);
        if (!generated)
        {
            var item = sink.Messages.Single().Value!.Json.Deserialize<SequenceItem>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            item.Name.ShouldBe("sequence");
            item.Value.ShouldBe(1);
            item.Step.ShouldBe(1);
            item.Sequence.ShouldBe(1);
        }
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void Null_binding_collection_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => SourcesComponentBindingsExtensions.AddSources(null!))
            .ParamName.ShouldBe("bindings");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Named_clock_requires_a_resolver_even_when_a_default_exists(bool generated)
    {
        var clock = new BorrowedClock();
        var bindings = new ComponentBindings().AddSources(clock);
        bindings.Add("test.sink", _ => new Sink());

        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            FlowJson.BuildAsync(Document(generated, "\"clock\":\"named\""), bindings));

        error.Message.ShouldContain("'named'");
        clock.DisposeCalls.ShouldBe(0);
    }

    private static string Document(bool generated, string options)
        => "{\"Resources\":{},\"Workflows\":{\"main\":{\"source\":{\"Type\":\"" + (generated ? "source.items" : "source.sequence") +
            "\",\"boundedCapacity\":2" + (string.IsNullOrEmpty(options) ? "" : "," + options) +
            ",\"Output\":\"sink.Input\"},\"sink\":{\"Type\":\"test.sink\"}}}}";

    private sealed class BorrowedClock : FakeTimeProvider, IAsyncDisposable
    {
        public BorrowedClock() : base(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) { }
        public int DisposeCalls { get; private set; }
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
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
        public int DisposeCalls { get; private set; }
        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public async ValueTask DisposeAsync() { DisposeCalls++; Complete(); await Completion; }
    }
}
