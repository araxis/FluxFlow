using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Core;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class DirectFileSourceGraphTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Direct_directory_source_uses_canonical_ports_and_drains_entries()
    {
        using var directory = TempDirectory.Create("direct-source");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "two.txt"), "second");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "skip.bin"), "skip");
        var source = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
        { Directory = ".", BaseDirectory = directory.Path, Filter = "*.txt", BoundedCapacity = 1 });
        AssertSourcePorts(source);
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("files");
        var sink = new RecordingSink();
        workflow.Add("enumerate", source).Output.LinkTo(workflow.Add("sink", sink).Input);
        sink.Items.ShouldBeEmpty();
        source.ProductionCompletion.IsCompleted.ShouldBeFalse();

        await app.StartAsync();
        await app.Completion.WaitAsync(Deadline);

        var entries = sink.Items.Select(message => message.Value!.Json.Deserialize<DirectoryEntry>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!).OrderBy(x => x.Name).ToArray();
        entries.Select(x => x.Name).ShouldBe(["one.txt", "two.txt"]);
        entries.Select(x => x.Length).ShouldBe(new long?[] { 3, 6 });
        entries.ShouldAllBe(x => x.EntryType == "File" && x.Directory == Path.GetFullPath(directory.Path));
        sink.Items.ShouldAllBe(x => !x.IsError && x.CorrelationId == null && !x.TraceId.IsEmpty);
        source.ProductionCompletion.IsCompletedSuccessfully.ShouldBeTrue();
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_file_watch_stops_or_faults_and_releases_watcher(bool fault)
    {
        using var directory = TempDirectory.Create("direct-watch");
        var source = new FileWatchNode(new FileWatchOptions
        { Directory = ".", BaseDirectory = directory.Path, BoundedCapacity = 32 });
        AssertSourcePorts(source);
        var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("files");
        var sink = new RecordingSink();
        workflow.Add("watch", source).Output.LinkTo(workflow.Add("sink", sink).Input);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new ActionBlock<FlowMessage>(message =>
        {
            if (message.Value!.Json.Deserialize<FlowEvent>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Name == FileWatchNode.WatchStarted) started.TrySetResult();
        });
        using var observation = source.Events.LinkTo(observer, new DataflowLinkOptions { PropagateCompletion = true });
        var failure = new InvalidOperationException("watch stopped by owner");
        try
        {
            await app.StartAsync();
            await started.Task.WaitAsync(Deadline);
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "created.txt"), "hello");
            await sink.First.Task.WaitAsync(Deadline);
            var change = sink.Items.First().Value!.Json.Deserialize<FileChange>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            change.Name.ShouldBe("created.txt");
            change.Path.ShouldBe(Path.Combine(directory.Path, "created.txt"));
            change.Directory.ShouldBe(Path.GetFullPath(directory.Path));
            if (fault)
            {
                source.Fault(failure);
                var error = await Record.ExceptionAsync(() => app.Completion.WaitAsync(Deadline));
                error.ShouldNotBeNull();
                error.ShouldNotBeOfType<TimeoutException>();
                (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Deadline)))
                    .ShouldBeSameAs(failure);
            }
            else await app.StopAsync().WaitAsync(Deadline);
            await observer.Completion.WaitAsync(Deadline);
            var count = sink.Items.Count;
            await File.WriteAllTextAsync(Path.Combine(directory.Path, "after-stop.txt"), "after");
            sink.Items.Count.ShouldBe(count);
            source.ProductionCompletion.IsCompleted.ShouldBeTrue();
            sink.Completion.IsCompleted.ShouldBeTrue();
        }
        finally
        {
            var cleanup = await Record.ExceptionAsync(() => app.DisposeAsync().AsTask().WaitAsync(Deadline));
            if (!fault) cleanup.ShouldBeNull();
            else cleanup.ShouldNotBeOfType<TimeoutException>();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_file_sources_can_stop_before_start_without_touching_files(bool watch)
    {
        // A nonexistent path makes accidental startup observable without creating IO.
        var missing = Path.Combine(Path.GetTempPath(), "fluxflow-missing-" + Guid.NewGuid().ToString("N"));
        FlowSource source = watch
            ? new FileWatchNode(new FileWatchOptions { Directory = ".", BaseDirectory = missing })
            : new DirectoryEnumerateNode(new DirectoryEnumerateOptions { Directory = ".", BaseDirectory = missing });
        await using var app = new FlowApplication();
        app.AddWorkflow("files").Add("source", source);
        await app.StopAsync().WaitAsync(Deadline);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        Directory.Exists(missing).ShouldBeFalse();
    }

    [Fact]
    public async Task Direct_directory_fault_releases_backpressured_production()
    {
        using var directory = TempDirectory.Create("direct-blocked-source");
        for (var i = 0; i < 20; i++) await File.WriteAllTextAsync(Path.Combine(directory.Path, $"{i}.txt"), "entry");
        var source = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
        { Directory = ".", BaseDirectory = directory.Path, BoundedCapacity = 1 });
        var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("files");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingSink(release.Task);
        workflow.Add("source", source).Output.LinkTo(workflow.Add("sink", sink).Input);
        var failure = new InvalidOperationException("stop blocked source");
        try
        {
            await app.StartAsync();
            await sink.First.Task.WaitAsync(Deadline);
            source.ProductionCompletion.IsCompleted.ShouldBeFalse();
            source.Fault(failure);
            (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            sink.Items.Count.ShouldBe(1);
            sink.Completion.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
            var error = await Record.ExceptionAsync(() => app.DisposeAsync().AsTask().WaitAsync(Deadline));
            error.ShouldBeOfType<AggregateException>();
            error.ShouldNotBeOfType<TimeoutException>();
        }
        sink.Completion.IsCompleted.ShouldBeTrue();
        source.ProductionCompletion.IsCompleted.ShouldBeTrue();
    }

    private static void AssertSourcePorts(FlowSource source)
    {
        source.Ports.Inputs.ShouldBeEmpty();
        source.Ports.Controls.ShouldBeEmpty();
        source.Ports.Outputs.ShouldHaveSingleItem().Key.ShouldBe("Output");
        source.Ports.Outputs["Output"].ShouldBeSameAs(source.Output);
        source.Ports.Events.ShouldBeSameAs(source.Events);
    }

    private sealed class RecordingSink : IFlowComponent
    {
        private readonly ActionBlock<FlowMessage> _input;
        public RecordingSink(Task? hold = null)
        {
            _input = new ActionBlock<FlowMessage>(async message =>
                { Items.Enqueue(message); First.TrySetResult(); if (hold is not null) await hold; },
                new ExecutionDataflowBlockOptions { BoundedCapacity = 1 });
            Ports = new FlowPorts(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });
        }
        public ConcurrentQueue<FlowMessage> Items { get; } = new();
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public ValueTask DisposeAsync() { Complete(); return ValueTask.CompletedTask; }
    }
}
