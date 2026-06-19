using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Xunit;
using static FluxFlow.Components.FileSystem.Tests.FileSystemTestHelpers;

namespace FluxFlow.Components.FileSystem.Tests;

// Every test news the node directly — no engine, no registry. Requests travel as
// FlowMessage<FileWriteRequest> envelopes; the correlation id flows request -> result.
public sealed class FileWriteNodeTests
{
    [Fact]
    public async Task FileWrite_WritesStringContentInsideBaseDirectory_PreservingCorrelation()
    {
        using var directory = TempDirectory.Create("write");
        await using var node = new FileWriteNode(new FileWriteOptions
        {
            BaseDirectory = directory.Path,
            BoundedCapacity = 4
        });
        var results = Sink(node.Output);

        var request = FlowMessage.Create(new FileWriteRequest
        {
            Path = "logs/output.txt",
            Content = "hello"
        });
        await node.Input.SendAsync(request);

        var result = await results.ReceiveAsync().WaitAsync(TestTimeout);
        result.CorrelationId.ShouldBe(request.CorrelationId);
        var expectedPath = Path.Combine(directory.Path, "logs", "output.txt");
        (await File.ReadAllTextAsync(expectedPath)).ShouldBe("hello");
        result.Payload.Path.ShouldBe(Path.GetFullPath(expectedPath));
        result.Payload.BytesWritten.ShouldBe(5);
        result.Payload.Mode.ShouldBe(FileWriteMode.Overwrite);
    }

    [Fact]
    public async Task FileWrite_UsesInjectedClockForResultTimestamp()
    {
        using var directory = TempDirectory.Create("write");
        var writtenAt = DateTimeOffset.Parse("2026-06-02T12:00:00Z");
        await using var node = new FileWriteNode(
            new FileWriteOptions { BaseDirectory = directory.Path },
            new FakeTimeProvider(writtenAt));
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileWriteRequest
        {
            Path = "clock.txt",
            Content = "hello"
        }));

        var result = await results.ReceiveAsync().WaitAsync(TestTimeout);
        result.Payload.WrittenAt.ShouldBe(writtenAt);
    }

    [Fact]
    public async Task FileWrite_WritesBytesAndAppendsInOrder()
    {
        using var directory = TempDirectory.Create("write");
        await using var node = new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path });
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileWriteRequest
        {
            Path = "data.bin",
            Bytes = [1, 2],
            Mode = FileWriteMode.Overwrite
        }));
        await node.Input.SendAsync(FlowMessage.Create(new FileWriteRequest
        {
            Path = "data.bin",
            Bytes = [3, 4],
            Mode = FileWriteMode.Append
        }));

        await results.ReceiveAsync().WaitAsync(TestTimeout);
        await results.ReceiveAsync().WaitAsync(TestTimeout);
        File.ReadAllBytes(Path.Combine(directory.Path, "data.bin")).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public async Task FileWrite_CreateNewReportsIoFailureAndContinues()
    {
        using var directory = TempDirectory.Create("write");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "target.txt"), "existing");
        await using var node = new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path });
        var results = Sink(node.Output);
        var errors = Sink(node.Errors);

        var failing = FlowMessage.Create(new FileWriteRequest
        {
            Path = "target.txt",
            Content = "first",
            Mode = FileWriteMode.CreateNew
        });
        await node.Input.SendAsync(failing);
        await node.Input.SendAsync(FlowMessage.Create(new FileWriteRequest
        {
            Path = "next.txt",
            Content = "second",
            Mode = FileWriteMode.CreateNew
        }));

        var error = await errors.ReceiveAsync().WaitAsync(TestTimeout);
        error.Code.ShouldBe(FileSystemErrorCodes.FileWriteIoFailed);
        error.CorrelationId.ShouldBe(failing.CorrelationId);
        var result = await results.ReceiveAsync().WaitAsync(TestTimeout);
        result.Payload.Path.ShouldEndWith("next.txt");
        (await File.ReadAllTextAsync(Path.Combine(directory.Path, "next.txt"))).ShouldBe("second");
    }

    [Fact]
    public async Task FileWrite_RejectsAbsolutePathWhenDisabled()
    {
        using var directory = TempDirectory.Create("write");
        await using var node = new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileWriteRequest
        {
            Path = Path.Combine(directory.Path, "blocked.txt"),
            Content = "blocked"
        }));

        var error = await errors.ReceiveAsync().WaitAsync(TestTimeout);
        error.Code.ShouldBe(FileSystemErrorCodes.FileWriteAbsolutePathDenied);
        File.Exists(Path.Combine(directory.Path, "blocked.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task FileWrite_RejectsRelativePathThatEscapesBaseDirectory()
    {
        using var directory = TempDirectory.Create("write");
        await using var node = new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileWriteRequest
        {
            Path = "../outside.txt",
            Content = "blocked"
        }));

        var error = await errors.ReceiveAsync().WaitAsync(TestTimeout);
        error.Code.ShouldBe(FileSystemErrorCodes.FileWriteInvalidPath);
        error.Message.ShouldContain("baseDirectory");
    }

    [Fact]
    public async Task FileWrite_UsesRequestEncoding()
    {
        using var directory = TempDirectory.Create("write");
        await using var node = new FileWriteNode(new FileWriteOptions
        {
            BaseDirectory = directory.Path,
            DefaultEncoding = "utf-8"
        });
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileWriteRequest
        {
            Path = "encoded.txt",
            Content = "A",
            Encoding = "utf-16"
        }));

        var result = await results.ReceiveAsync().WaitAsync(TestTimeout);
        result.Payload.BytesWritten.ShouldBe(2);
        File.ReadAllBytes(Path.Combine(directory.Path, "encoded.txt"))
            .ShouldBe(Encoding.Unicode.GetBytes("A"));
    }

    [Fact]
    public async Task FileWrite_ReportsUnsupportedEncoding()
    {
        using var directory = TempDirectory.Create("write");
        await using var node = new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileWriteRequest
        {
            Path = "encoded.txt",
            Content = "A",
            Encoding = "not-a-real-encoding"
        }));

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.FileWriteUnsupportedEncoding);
    }

    [Fact]
    public async Task FileWrite_ReportsMissingContent()
    {
        using var directory = TempDirectory.Create("write");
        await using var node = new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileWriteRequest { Path = "empty.txt" }));

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.FileWriteContentMissing);
    }

    [Fact]
    public async Task FileWrite_SuccessEmitsEventCarryingCorrelationId()
    {
        using var directory = TempDirectory.Create("write");
        await using var node = new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path });
        Sink(node.Output);
        var events = Sink(node.Events);

        var request = FlowMessage.Create(new FileWriteRequest { Path = "diag.txt", Content = "value" });
        await node.Input.SendAsync(request);

        var @event = await events.ReceiveAsync().WaitAsync(TestTimeout);
        @event.Name.ShouldBe(FileWriteNode.WriteSucceeded);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(request.CorrelationId);
        @event.Attributes["bytesWritten"].ShouldBe(5);
    }

    [Fact]
    public async Task FileWrite_CompletesOutput()
    {
        using var directory = TempDirectory.Create("write");
        await using var node = new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path });
        var results = Sink(node.Output);

        node.Complete();
        await node.Completion.WaitAsync(TestTimeout);
        await results.Completion.WaitAsync(TestTimeout);
        results.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void FileWrite_RejectsInvalidBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new FileWriteNode(new FileWriteOptions { BoundedCapacity = 0 }));

    [Fact]
    public void FileWrite_RejectsUnsupportedDefaultEncoding()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new FileWriteNode(new FileWriteOptions { DefaultEncoding = "not-a-real-encoding" }));
        exception.Message.ShouldContain("defaultEncoding");
    }
}
