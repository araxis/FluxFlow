using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;
using static FluxFlow.Components.FileSystem.Tests.FileSystemTestHelpers;

namespace FluxFlow.Components.FileSystem.Tests;

// file.watch is an event-driven FlowSource: StartAsync arms a FileSystemWatcher, then
// real filesystem changes drive its output. The injected clock stamps the timestamps.
public sealed class FileWatchNodeTests
{
    [Fact]
    public async Task FileWatch_EmitsFileEvents_WithFreshCorrelation()
    {
        using var directory = TempDirectory.Create("watch");
        await using var node = new FileWatchNode(new FileWatchOptions
        {
            Directory = ".",
            BaseDirectory = directory.Path,
            BoundedCapacity = 16
        });
        var output = Sink(node.Output);

        await node.StartAsync();
        var filePath = Path.Combine(directory.Path, "created.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var watchEvent = await ReceiveMatchingAsync(
            output,
            value => value["name"].GetString() == "created.txt" &&
                     value["changeType"].GetString() is "Created" or "Changed");

        watchEvent.Payload.GetObject()["path"].GetString().ShouldBe(Path.GetFullPath(filePath));
        watchEvent.Payload.GetObject()["directory"].GetString()
            .ShouldBe(Path.GetFullPath(directory.Path));
        watchEvent.CorrelationId.IsEmpty.ShouldBeFalse();

        node.Complete();
        await node.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task FileWatch_EmitsRenamedEvents()
    {
        using var directory = TempDirectory.Create("watch");
        var originalPath = Path.Combine(directory.Path, "before.txt");
        var renamedPath = Path.Combine(directory.Path, "after.txt");
        await File.WriteAllTextAsync(originalPath, "hello");
        await using var node = new FileWatchNode(new FileWatchOptions
        {
            Directory = ".",
            BaseDirectory = directory.Path,
            BoundedCapacity = 16
        });
        var output = Sink(node.Output);

        await node.StartAsync();
        File.Move(originalPath, renamedPath);

        var watchEvent = (await ReceiveMatchingAsync(
            output,
            value => value["name"].GetString() == "after.txt" &&
                     value["changeType"].GetString() == "Renamed")).Payload.GetObject();

        watchEvent["path"].GetString().ShouldBe(Path.GetFullPath(renamedPath));
        watchEvent["oldPath"].GetString().ShouldBe(Path.GetFullPath(originalPath));
        watchEvent["oldName"].GetString().ShouldBe("before.txt");

        node.Complete();
        await node.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task FileWatch_StampsEventsWithInjectedClockAndEmitsLifecycleEvents()
    {
        using var directory = TempDirectory.Create("watch");
        var timestamp = DateTimeOffset.Parse("2026-06-02T12:30:00Z");
        await using var node = new FileWatchNode(
            new FileWatchOptions
            {
                Directory = ".",
                BaseDirectory = directory.Path,
                BoundedCapacity = 16
            },
            new FakeTimeProvider(timestamp));
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        await node.StartAsync();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "diag.txt"), "hello");
        var watchEvent = await ReceiveMatchingAsync(
            output,
            value => value["name"].GetString() == "diag.txt");
        watchEvent.Payload.GetObject()["timestamp"].GetDateTimeOffset().ShouldBe(timestamp);

        await ReceiveEventAsync(events, FileWatchNode.WatchStarted);
        var changed = await ReceiveEventAsync(events, FileWatchNode.WatchChanged);
        changed.Message.ShouldNotBeNull().ShouldContain("diag.txt");
        changed.Timestamp.ShouldBe(timestamp);

        node.Complete();
        await node.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task FileWatch_CompleteStopsAndCompletesOutput()
    {
        using var directory = TempDirectory.Create("watch");
        await using var node = new FileWatchNode(new FileWatchOptions
        {
            Directory = ".",
            BaseDirectory = directory.Path
        });
        var output = Sink(node.Output);

        await node.StartAsync();
        node.Complete();
        await node.Completion.WaitAsync(TestTimeout);

        await output.Completion.WaitAsync(TestTimeout);
        output.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task FileWatch_MissingDirectoryFaultsCompletion()
    {
        using var directory = TempDirectory.Create("watch");
        await using var node = new FileWatchNode(new FileWatchOptions
        {
            Directory = "missing",
            BaseDirectory = directory.Path
        });
        await node.StartAsync();
        var failure = await Should.ThrowAsync<FileSystemSourceException>(
            () => node.Completion.WaitAsync(TestTimeout));
        failure.ErrorCode.ShouldBe(FileSystemErrorCodes.FileWatchDirectoryMissing);
        node.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public async Task FileWatch_RejectsAbsoluteDirectoryWhenDisabled()
    {
        using var directory = TempDirectory.Create("watch");
        await using var node = new FileWatchNode(new FileWatchOptions
        {
            Directory = directory.Path,
            BaseDirectory = directory.Path
        });
        await node.StartAsync();
        var failure = await Should.ThrowAsync<FileSystemSourceException>(
            () => node.Completion.WaitAsync(TestTimeout));
        failure.ErrorCode.ShouldBe(FileSystemErrorCodes.FileWatchAbsolutePathDenied);
        node.Completion.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public void FileWatch_RejectsMissingDirectoryOption()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new FileWatchNode(new FileWatchOptions()));
        exception.Message.ShouldContain("directory");
    }

    [Fact]
    public void FileWatch_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new FileWatchNode(new FileWatchOptions { Directory = ".", BoundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void FileWatch_RejectsInternalBufferSizeOutsideRange()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new FileWatchNode(new FileWatchOptions { Directory = ".", InternalBufferSize = 1024 }));

        exception.Message.ShouldContain("internalBufferSize");
    }

    [Fact]
    public async Task FileWatch_StartsWithConfiguredInternalBufferSize()
    {
        using var directory = TempDirectory.Create("watch");
        await using var node = new FileWatchNode(new FileWatchOptions
        {
            Directory = ".",
            BaseDirectory = directory.Path,
            InternalBufferSize = 16384
        });
        var output = Sink(node.Output);

        await node.StartAsync();
        node.Complete();
        await node.Completion.WaitAsync(TestTimeout);
        output.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void FileWatch_RejectsUnsupportedNotifyFilter()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new FileWatchNode(new FileWatchOptions
            {
                Directory = ".",
                NotifyFilters = ["DefinitelyNotAFilter"]
            }));
        exception.Message.ShouldContain("notifyFilters");
    }

    private static async Task<FlowMessage<FlowValue>> ReceiveMatchingAsync(
        BufferBlock<FlowMessage<FlowValue>> output,
        Func<IReadOnlyDictionary<string, FlowValue>, bool> predicate)
    {
        using var cancellation = new CancellationTokenSource(TestTimeout);
        while (!cancellation.IsCancellationRequested)
        {
            var value = await output.ReceiveAsync(cancellation.Token);
            if (predicate(value.Payload.GetObject()))
            {
                return value;
            }
        }

        throw new TimeoutException("Timed out waiting for file watch event.");
    }

    private static async Task<FlowEvent> ReceiveEventAsync(BufferBlock<FlowEvent> events, string name)
    {
        using var cancellation = new CancellationTokenSource(TestTimeout);
        while (!cancellation.IsCancellationRequested)
        {
            var value = await events.ReceiveAsync(cancellation.Token);
            if (value.Name == name)
            {
                return value;
            }
        }

        throw new TimeoutException($"Timed out waiting for event '{name}'.");
    }
}
