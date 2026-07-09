using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;
using static FluxFlow.Components.FileSystem.Tests.FileSystemTestHelpers;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class FileSystemPathConfinementTests
{
    [Fact]
    public async Task FileRead_RejectsLinkedDescendantUnderBaseDirectory()
    {
        using var outside = TempDirectory.Create("outside-read");
        using var directory = TempDirectory.Create("base-read");
        await File.WriteAllTextAsync(Path.Combine(outside.Path, "value.txt"), "outside");
        if (!TryCreateDirectoryLink(Path.Combine(directory.Path, "linked"), outside.Path))
            return;

        await using var node = new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest { Path = "linked/value.txt" }));

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.FileReadInvalidPath);
    }

    [Fact]
    public async Task FileWrite_RejectsLinkedDescendantUnderBaseDirectory()
    {
        using var outside = TempDirectory.Create("outside-write");
        using var directory = TempDirectory.Create("base-write");
        if (!TryCreateDirectoryLink(Path.Combine(directory.Path, "linked"), outside.Path))
            return;

        await using var node = new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileWriteRequest
        {
            Path = "linked/value.txt",
            Content = "blocked"
        }));

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.FileWriteInvalidPath);
        File.Exists(Path.Combine(outside.Path, "value.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task DirectoryEnumerate_RejectsLinkedDescendantUnderBaseDirectory()
    {
        using var outside = TempDirectory.Create("outside-enumerate");
        using var directory = TempDirectory.Create("base-enumerate");
        if (!TryCreateDirectoryLink(Path.Combine(directory.Path, "linked"), outside.Path))
            return;

        await using var node = new DirectoryEnumerateNode(new DirectoryEnumerateOptions
        {
            Directory = "linked",
            BaseDirectory = directory.Path
        });
        var errors = Sink(node.Errors);

        await node.StartAsync();
        await node.Completion.WaitAsync(TestTimeout);

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.DirectoryEnumerateInvalidDirectory);
    }

    [Fact]
    public async Task FileWatch_RejectsLinkedDescendantUnderBaseDirectory()
    {
        using var outside = TempDirectory.Create("outside-watch");
        using var directory = TempDirectory.Create("base-watch");
        if (!TryCreateDirectoryLink(Path.Combine(directory.Path, "linked"), outside.Path))
            return;

        await using var node = new FileWatchNode(new FileWatchOptions
        {
            Directory = "linked",
            BaseDirectory = directory.Path
        });
        var errors = Sink(node.Errors);

        await node.StartAsync();
        await node.Completion.WaitAsync(TestTimeout);

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.FileWatchInvalidDirectory);
    }

    private static bool TryCreateDirectoryLink(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
