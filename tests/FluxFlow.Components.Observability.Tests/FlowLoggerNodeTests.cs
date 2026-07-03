using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Observability.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the correlation id flows input -> entry and onto any
// error/event for free.
public sealed class FlowLoggerNodeTests
{
    [Fact]
    public async Task Logger_EmitsStructuredEntryPreservingCorrelationId()
    {
        var timestamp = new DateTimeOffset(2026, 6, 2, 18, 30, 0, TimeSpan.Zero);
        await using var node = new FlowLoggerNode<InputMessage>(
            new FlowLoggerOptions
            {
                InputType = "message",
                Level = "Warning",
                Category = "workflow.test",
                MessageTemplate = "Observed {kind}:{size} #{sequence}"
            },
            Selectors(
                ("kind", (InputMessage message) => message.Kind),
                ("size", message => message.Payload.Length)),
            new FakeTimeProvider(timestamp));
        var entries = Sink(node.Output);

        var sent = FlowMessage.Create(new InputMessage("alpha", [1, 2, 3], true));
        await node.Input.SendAsync(sent);

        var entry = await entries.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        entry.CorrelationId.ShouldBe(sent.CorrelationId);   // the whole point of the envelope
        entry.Payload.Level.ShouldBe(FlowLogLevel.Warning);
        entry.Payload.Category.ShouldBe("workflow.test");
        entry.Payload.Message.ShouldBe("Observed alpha:3 #1");
        entry.Payload.InputType.ShouldBe("message");
        entry.Payload.Sequence.ShouldBe(1);
        entry.Payload.Timestamp.ShouldBe(timestamp);
        entry.Payload.Attributes["kind"].ShouldBe("alpha");
        entry.Payload.Attributes["size"].ShouldBe(3);
    }

    [Fact]
    public async Task Output_FansOutEveryEntryToEveryConsumer()
    {
        // One node's output linked to two downstream consumers, no engine. Both see
        // every entry.
        await using var node = new FlowLoggerNode<string>(
            new FlowLoggerOptions { InputType = "string", MessageTemplate = "{input}" });
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create("one"));
        await node.Input.SendAsync(FlowMessage.Create("two"));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Message.ShouldBe("one");
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Message.ShouldBe("two");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Message.ShouldBe("one");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Message.ShouldBe("two");
    }

    [Fact]
    public async Task Logger_ReportsAttributeSelectorFailureAndStillEmitsEntry()
    {
        await using var node = new FlowLoggerNode<InputMessage>(
            new FlowLoggerOptions
            {
                InputType = "message",
                AttributeSelectors = ["kind", "broken"]
            },
            Selectors(
                ("kind", (InputMessage message) => message.Kind),
                ("broken", _ => throw new InvalidOperationException("select failed"))));
        var entries = Sink(node.Output);
        var errors = Sink(node.Errors);

        var sent = FlowMessage.Create(new InputMessage("alpha", [1], true));
        await node.Input.SendAsync(sent);

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ObservabilityErrorCodes.LoggerAttributeSelectorFailed);
        error.CorrelationId.ShouldBe(sent.CorrelationId);
        error.Context!.ShouldContain("selector=broken");

        var entry = await entries.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        entry.Payload.Attributes["kind"].ShouldBe("alpha");
        entry.Payload.Attributes.ContainsKey("broken").ShouldBeFalse();
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Logger_DoesNotExpandPlaceholdersFromSubstitutedValues()
    {
        await using var node = new FlowLoggerNode<InputMessage>(
            new FlowLoggerOptions
            {
                InputType = "message",
                MessageTemplate = "Observed {kind}"
            },
            Selectors(("kind", (InputMessage message) => message.Kind)));
        var entries = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new InputMessage("{category}", [1], true)));

        var entry = await entries.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        entry.Payload.Message.ShouldBe("Observed {category}");
    }

    [Fact]
    public async Task Logger_EmitsEventCarryingCorrelationId()
    {
        await using var node = new FlowLoggerNode<string>(
            new FlowLoggerOptions { InputType = "string", Category = "workflow.test" });
        Sink(node.Output);
        var events = Sink(node.Events);

        var sent = FlowMessage.Create("hello");
        await node.Input.SendAsync(sent);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(FlowLoggerNode<string>.Emitted);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(sent.CorrelationId);
        @event.Attributes["name"].ShouldBe("workflow.test");
    }

    [Fact]
    public void Logger_RejectsUnsupportedLevel()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => new FlowLoggerNode<string>(
                new FlowLoggerOptions { InputType = "string", Level = "Nope" }));

        exception.Message.ShouldContain("level");
    }

    [Fact]
    public async Task Logger_TreatsNoAttributeSelectorsAsEmpty()
    {
        await using var node = new FlowLoggerNode<string>(
            new FlowLoggerOptions { InputType = "string" });
        var entries = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create("hello"));

        var entry = await entries.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        entry.Payload.Attributes.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_RequiresOptions()
        => Should.Throw<ArgumentNullException>(() => new FlowLoggerNode<string>(null!));

    [Fact]
    public void Constructor_RequiresNonEmptyInputType()
        => Should.Throw<ArgumentException>(
            () => new FlowLoggerNode<string>(
                new FlowLoggerOptions { InputType = " " }));

    [Fact]
    public void Constructor_RequiresPositiveBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowLoggerNode<string>(
                new FlowLoggerOptions { BoundedCapacity = 0 }));

    private static IReadOnlyDictionary<string, IObservabilityValueSelector<TInput>> Selectors<TInput>(
        params (string Name, Func<TInput, object?> Select)[] selectors)
        => selectors.ToDictionary(
            selector => selector.Name,
            selector => (IObservabilityValueSelector<TInput>)new DelegateSelector<TInput>(
                (input, _) => selector.Select(input)),
            StringComparer.Ordinal);

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private sealed class DelegateSelector<TInput>(
        Func<TInput, ObservabilityNodeContext, object?> selector)
        : IObservabilityValueSelector<TInput>
    {
        public object? Select(TInput input, ObservabilityNodeContext context)
            => selector(input, context);
    }

    private sealed record InputMessage(string Kind, byte[] Payload, bool Enabled);
}
