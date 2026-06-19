using FluxFlow.Components.Sources;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sources.Tests;

public sealed class GeneratedSourceNodeTests
{
    [Fact]
    public async Task Generated_EmitsTypedConfiguredItems()
    {
        await using var node = new GeneratedSourceNode<InputMessage>(
            new GeneratedSourceOptions { OutputType = "app.input", BoundedCapacity = 8 },
            new[]
            {
                new InputMessage("A-100", 10),
                new InputMessage("A-101", 20)
            });
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var items = await SourcesTestSink.DrainUntilCompletedAsync(output);
        items.Select(message => message.Payload.Id).ShouldBe(["A-100", "A-101"]);
        items.Select(message => message.Payload.Value).ShouldBe([10, 20]);
    }

    [Fact]
    public async Task Generated_MintsAFreshCorrelationIdPerItem()
    {
        await using var node = new GeneratedSourceNode<int>(
            new GeneratedSourceOptions { OutputType = "int" },
            new[] { 1, 2, 3 });
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var items = await SourcesTestSink.DrainUntilCompletedAsync(output);
        items.Select(message => message.Payload).ShouldBe([1, 2, 3]);
        items.Select(message => message.CorrelationId).Distinct().Count().ShouldBe(3);
        items.ShouldAllBe(message => !message.CorrelationId.IsEmpty);
    }

    [Fact]
    public async Task Generated_LoopsUntilMaxItems()
    {
        await using var node = new GeneratedSourceNode<int>(
            new GeneratedSourceOptions { OutputType = "int", Loop = true, MaxItems = 5 },
            new[] { 1, 2 });
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var items = await SourcesTestSink.DrainUntilCompletedAsync(output);
        items.Select(message => message.Payload).ShouldBe([1, 2, 1, 2, 1]);
    }

    [Fact]
    public async Task Generated_UsesConfiguredClockForTiming()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new GeneratedSourceNode<string>(
            new GeneratedSourceOptions
            {
                OutputType = "string",
                InitialDelayMilliseconds = 15,
                IntervalMilliseconds = 30
            },
            new[] { "one", "two" },
            clock);
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();

        // The node holds a 15ms initial delay and then a 30ms interval delay between the
        // two items. FakeTimeProvider keeps each Task.Delay pending until time advances,
        // so step the clock forward until the run loop drains and completes.
        await AdvanceUntilCompletedAsync(clock, node, TimeSpan.FromMilliseconds(30));

        (await SourcesTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload)
            .ShouldBe(["one", "two"]);
    }

    [Fact]
    public async Task Generated_CompletesEmptyItemList()
    {
        await using var node = new GeneratedSourceNode<string>(
            new GeneratedSourceOptions { OutputType = "string" },
            Array.Empty<string>());
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await SourcesTestSink.DrainUntilCompletedAsync(output)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Generated_EmitsLifecycleEvents()
    {
        await using var node = new GeneratedSourceNode<string>(
            new GeneratedSourceOptions { Name = "demo", OutputType = "string" },
            new[] { "one" });
        var output = SourcesTestSink.Link(node.Output);
        var events = SourcesTestSink.Link(node.Events);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await SourcesTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var names = (await SourcesTestSink.DrainUntilCompletedAsync(events))
            .Select(flowEvent => flowEvent.Name)
            .ToArray();
        names.ShouldContain(GeneratedSourceNode<string>.Started);
        names.ShouldContain(GeneratedSourceNode<string>.Emitted);
        names.ShouldContain(GeneratedSourceNode<string>.Completed);
    }

    [Fact]
    public async Task Generated_CompleteBeforeStartCompletesOutput()
    {
        await using var node = new GeneratedSourceNode<string>(
            new GeneratedSourceOptions { OutputType = "string" },
            new[] { "one" });
        var output = SourcesTestSink.Link(node.Output);

        node.Complete();
        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Generated_FailureReportsErrorAndFaults()
    {
        var failure = new InvalidOperationException("boom");
        await using var node = new GeneratedSourceNode<string>(
            new GeneratedSourceOptions { OutputType = "string" },
            new ThrowingList(failure));
        var output = SourcesTestSink.Link(node.Output);
        var errors = SourcesTestSink.Link(node.Errors);

        await node.StartAsync();

        // The source faults: Output is faulted, but Errors is completed (flushed) so the
        // buffered FlowError survives — mirror the kit fault rule.
        var completion = await Should.ThrowAsync<InvalidOperationException>(
            () => node.Completion.WaitAsync(TimeSpan.FromSeconds(30)));
        completion.ShouldBeSameAs(failure);

        var reported = (await SourcesTestSink.DrainUntilCompletedAsync(errors)).ShouldHaveSingleItem();
        reported.Code.ShouldBe(SourceErrorCodes.GeneratedFailed);
        reported.Exception.ShouldBeSameAs(failure);
        reported.CorrelationId.ShouldBeNull();

        // Output is faulted (a Dataflow block wraps the fault in an AggregateException).
        await Should.ThrowAsync<Exception>(
            () => output.Completion.WaitAsync(TimeSpan.FromSeconds(30)));
        output.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public void Generated_ConstructorRejectsLoopWithoutMaxItems()
        => Should.Throw<ArgumentException>(
                () => new GeneratedSourceNode<string>(
                    new GeneratedSourceOptions { Loop = true },
                    ["one"]))
            .Message.ShouldContain("maxItems");

    [Fact]
    public void Generated_ConstructorRejectsNonPositiveMaxItems()
        => Should.Throw<ArgumentException>(
                () => new GeneratedSourceNode<string>(
                    new GeneratedSourceOptions { MaxItems = 0 },
                    ["one"]))
            .Message.ShouldContain("maxItems");

    [Fact]
    public void Generated_ConstructorRejectsInvalidCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
                () => new GeneratedSourceNode<string>(
                    new GeneratedSourceOptions { BoundedCapacity = 0 },
                    ["one"]))
            .Message.ShouldContain("capacity");

    [Fact]
    public void Generated_ConstructorRejectsNullItems()
        => Should.Throw<ArgumentNullException>(
            () => new GeneratedSourceNode<string>(new GeneratedSourceOptions(), null!));

    [Fact]
    public void Generated_ConstructorRejectsNullOptions()
        => Should.Throw<ArgumentNullException>(
            () => new GeneratedSourceNode<string>(null!, ["one"]));

    // FakeTimeProvider leaves each Task.Delay pending until time advances, and the run
    // loop registers its delays asynchronously. This drains them deterministically:
    // advance exactly once for each newly created timer, gating on the wrapper's created
    // count (and its registration signal) rather than polling with sleeps. Counting
    // created timers avoids the lost-wakeup where a timer is armed before the test looks.
    private static async Task AdvanceUntilCompletedAsync(
        TrackingFakeTimeProvider clock,
        IFlowNode node,
        TimeSpan step)
    {
        var fired = 0;
        while (!node.Completion.IsCompleted)
        {
            var scheduled = clock.TimerScheduled;

            if (clock.CreatedTimerCount > fired)
            {
                clock.Advance(step);
                fired++;
                continue;
            }

            await Task.WhenAny(scheduled, node.Completion)
                .WaitAsync(TimeSpan.FromSeconds(30));
        }

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed record InputMessage(string Id, int Value);

    // A non-empty item list whose indexer throws, to drive the node's failure path
    // deterministically (Count > 0 so the run loop enters and reaches the throwing read).
    private sealed class ThrowingList(Exception failure) : IReadOnlyList<string>
    {
        public string this[int index] => throw failure;

        public int Count => 1;

        public IEnumerator<string> GetEnumerator() => throw failure;

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => throw failure;
    }
}
