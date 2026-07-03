using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;
using static FluxFlow.Components.FileSystem.Tests.FileSystemTestHelpers;

namespace FluxFlow.Components.FileSystem.Tests;

// directory.enumerate is a FlowSource: StartAsync, then drain Output until it completes.
// Each entry is minted as a fresh FlowMessage<DirectoryEnumerateEntry>.
public sealed class DirectoryEnumerateNodeTests
{
    [Fact]
    public async Task DirectoryEnumerate_EmitsMatchingFilesInsideBaseDirectory()
    {
        using var directory = TempDirectory.Create("enumerate");
        Directory.CreateDirectory(Path.Combine(directory.Path, "nested"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "nested", "child.txt"), "child");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "skip.bin"), "skip");
        await using var node = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
        {
            Directory = ".",
            BaseDirectory = directory.Path,
            Filter = "*.txt",
            IncludeSubdirectories = true,
            BoundedCapacity = 8
        });
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TestTimeout);

        var entries = (await DrainAsync(output)).Select(message => message.Payload).ToList();
        entries.Select(entry => entry.Name).Order().ShouldBe(["child.txt", "root.txt"]);
        entries.ShouldAllBe(entry => entry.EntryType == DirectoryEntryType.File);
        entries.ShouldAllBe(entry => entry.Directory == Path.GetFullPath(directory.Path));
        entries.Single(entry => entry.Name == "root.txt").Length.ShouldBe(4);
    }

    [Fact]
    public async Task DirectoryEnumerate_UsesInjectedClockForEntryTimestamp()
    {
        using var directory = TempDirectory.Create("enumerate");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "one.txt"), "one");
        var enumeratedAt = DateTimeOffset.Parse("2026-06-02T12:20:00Z");
        await using var node = new DirectoryEnumerateNode(
            new DirectoryEnumerateOptions { Directory = ".", BaseDirectory = directory.Path },
            new FakeTimeProvider(enumeratedAt));
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TestTimeout);

        var entry = (await DrainAsync(output)).ShouldHaveSingleItem();
        entry.Payload.EnumeratedAt.ShouldBe(enumeratedAt);
    }

    [Fact]
    public async Task DirectoryEnumerate_CanEmitDirectories()
    {
        using var directory = TempDirectory.Create("enumerate");
        Directory.CreateDirectory(Path.Combine(directory.Path, "nested"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "root.txt"), "root");
        await using var node = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
        {
            Directory = ".",
            BaseDirectory = directory.Path,
            IncludeFiles = false,
            IncludeDirectories = true,
            BoundedCapacity = 4
        });
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TestTimeout);

        var entry = (await DrainAsync(output)).ShouldHaveSingleItem().Payload;
        entry.Name.ShouldBe("nested");
        entry.EntryType.ShouldBe(DirectoryEntryType.Directory);
        entry.Length.ShouldBeNull();
    }

    [Fact]
    public async Task DirectoryEnumerate_MaxEntriesLimitsOutput()
    {
        using var directory = TempDirectory.Create("enumerate");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "two.txt"), "two");
        await using var node = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
        {
            Directory = ".",
            BaseDirectory = directory.Path,
            MaxEntries = 1
        });
        var output = Sink(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TestTimeout);

        (await DrainAsync(output)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task DirectoryEnumerate_EmitsLifecycleEvents()
    {
        using var directory = TempDirectory.Create("enumerate");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "diag.txt"), "value");
        await using var node = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
        {
            Directory = ".",
            BaseDirectory = directory.Path
        });
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        await node.StartAsync();
        await node.Completion.WaitAsync(TestTimeout);

        (await DrainAsync(output)).ShouldHaveSingleItem();
        var eventNames = (await DrainEventsAsync(events)).Select(value => value.Name).ToArray();
        eventNames.ShouldContain(DirectoryEnumerateNode.EnumerateStarted);
        eventNames.ShouldContain(DirectoryEnumerateNode.EnumerateEntry);
        eventNames.ShouldContain(DirectoryEnumerateNode.EnumerateCompleted);
    }

    [Fact]
    public async Task DirectoryEnumerate_MissingDirectoryReportsErrorAndCompletes()
    {
        using var directory = TempDirectory.Create("enumerate");
        await using var node = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
        {
            Directory = "missing",
            BaseDirectory = directory.Path
        });
        var errors = Sink(node.Errors);

        await node.StartAsync();
        await node.Completion.WaitAsync(TestTimeout);

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.DirectoryEnumerateDirectoryMissing);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task DirectoryEnumerate_RejectsAbsoluteDirectoryWhenDisabled()
    {
        using var directory = TempDirectory.Create("enumerate");
        await using var node = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
        {
            Directory = directory.Path,
            BaseDirectory = directory.Path
        });
        var errors = Sink(node.Errors);

        await node.StartAsync();
        await node.Completion.WaitAsync(TestTimeout);

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.DirectoryEnumerateAbsolutePathDenied);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public void DirectoryEnumerate_RejectsMissingDirectoryOption()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new DirectoryEnumerateNode(new DirectoryEnumerateOptions()));
        exception.Message.ShouldContain("directory");
    }

    [Fact]
    public void DirectoryEnumerate_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new DirectoryEnumerateNode(new DirectoryEnumerateOptions
            {
                Directory = ".",
                BoundedCapacity = 0
            }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void DirectoryEnumerate_RejectsDisabledEntryTypes()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new DirectoryEnumerateNode(new DirectoryEnumerateOptions
            {
                Directory = ".",
                IncludeFiles = false,
                IncludeDirectories = false
            }));
        exception.Message.ShouldContain("includeFiles");
    }

    [Fact]
    public void DirectoryEnumerate_RejectsInvalidMaxEntries()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new DirectoryEnumerateNode(new DirectoryEnumerateOptions
            {
                Directory = ".",
                MaxEntries = 0
            }));

        exception.Message.ShouldContain("maxEntries");
    }

    private static async Task<List<FlowEvent>> DrainEventsAsync(BufferBlock<FlowEvent> events)
    {
        using var cancellation = new CancellationTokenSource(TestTimeout);
        var collected = new List<FlowEvent>();
        while (await events.OutputAvailableAsync(cancellation.Token))
        {
            while (events.TryReceive(out var value))
            {
                collected.Add(value);
            }
        }

        return collected;
    }
}
