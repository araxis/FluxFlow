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

// Every test news the node directly — no engine. Requests travel as
// FlowMessage<FileReadRequest> envelopes; the correlation id flows request -> result.
public sealed class FileReadNodeTests
{
    [Fact]
    public async Task FileRead_ReadsTextInsideBaseDirectory_PreservingCorrelation()
    {
        using var directory = TempDirectory.Create("read");
        Directory.CreateDirectory(Path.Combine(directory.Path, "logs"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "logs", "input.txt"), "hello");
        await using var node = new FileReadNode(new FileReadOptions
        {
            BaseDirectory = directory.Path,
            BoundedCapacity = 4
        });
        var results = Sink(node.Output);

        var request = FlowMessage.Create(new FileReadRequest { Path = "logs/input.txt" });
        await node.Input.SendAsync(request);

        var result = await results.ReceiveAsync().WaitAsync(TestTimeout);
        result.CorrelationId.ShouldBe(request.CorrelationId);
        var expectedPath = Path.Combine(directory.Path, "logs", "input.txt");
        result.Payload.Path.ShouldBe(Path.GetFullPath(expectedPath));
        result.Payload.Content.ShouldBe("hello");
        result.Payload.Bytes.ShouldBeNull();
        result.Payload.BytesRead.ShouldBe(5);
        result.Payload.ReadAs.ShouldBe(FileReadMode.Text);
        result.Payload.Encoding.ShouldBe("utf-8");
    }

    [Fact]
    public async Task FileRead_UsesInjectedClockForResultTimestamp()
    {
        using var directory = TempDirectory.Create("read");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "input.txt"), "hello");
        var readAt = DateTimeOffset.Parse("2026-06-02T12:10:00Z");
        await using var node = new FileReadNode(
            new FileReadOptions { BaseDirectory = directory.Path },
            new FakeTimeProvider(readAt));
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest { Path = "input.txt" }));

        (await results.ReceiveAsync().WaitAsync(TestTimeout)).Payload.ReadAt.ShouldBe(readAt);
    }

    [Fact]
    public async Task FileRead_ReadsBytes()
    {
        using var directory = TempDirectory.Create("read");
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "data.bin"), [1, 2, 3]);
        await using var node = new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest
        {
            Path = "data.bin",
            ReadAs = FileReadMode.Bytes
        }));

        var result = (await results.ReceiveAsync().WaitAsync(TestTimeout)).Payload;
        result.Bytes.ShouldBe([1, 2, 3]);
        result.Content.ShouldBeNull();
        result.Encoding.ShouldBeNull();
        result.BytesRead.ShouldBe(3);
        result.ReadAs.ShouldBe(FileReadMode.Bytes);
    }

    [Fact]
    public async Task FileRead_MissingFileReportsErrorAndContinues()
    {
        using var directory = TempDirectory.Create("read");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "next.txt"), "second");
        await using var node = new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        var results = Sink(node.Output);
        var errors = Sink(node.Errors);

        var missing = FlowMessage.Create(new FileReadRequest { Path = "missing.txt" });
        await node.Input.SendAsync(missing);
        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest { Path = "next.txt" }));

        var error = await errors.ReceiveAsync().WaitAsync(TestTimeout);
        error.Code.ShouldBe(FileSystemErrorCodes.FileReadNotFound);
        error.CorrelationId.ShouldBe(missing.CorrelationId);
        (await results.ReceiveAsync().WaitAsync(TestTimeout)).Payload.Content.ShouldBe("second");
    }

    [Fact]
    public async Task FileRead_RejectsAbsolutePathWhenDisabled()
    {
        using var directory = TempDirectory.Create("read");
        var filePath = Path.Combine(directory.Path, "blocked.txt");
        await File.WriteAllTextAsync(filePath, "blocked");
        await using var node = new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest { Path = filePath }));

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.FileReadAbsolutePathDenied);
    }

    [Fact]
    public async Task FileRead_RejectsRelativePathThatEscapesBaseDirectory()
    {
        using var directory = TempDirectory.Create("read");
        await using var node = new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest { Path = "../outside.txt" }));

        var error = await errors.ReceiveAsync().WaitAsync(TestTimeout);
        error.Code.ShouldBe(FileSystemErrorCodes.FileReadInvalidPath);
        error.Message.ShouldContain("baseDirectory");
    }

    [Fact]
    public async Task FileRead_RejectsRelativePathThatEscapesWorkingDirectoryByDefault()
    {
        await using var node = new FileReadNode();
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest { Path = "../outside.txt" }));

        var error = await errors.ReceiveAsync().WaitAsync(TestTimeout);
        error.Code.ShouldBe(FileSystemErrorCodes.FileReadInvalidPath);
        error.Message.ShouldContain("working directory");
    }

    [Fact]
    public async Task FileRead_ReadsWorkingDirectoryRelativePathByDefault()
    {
        var fileName = $"fluxflow-read-cwd-{Guid.NewGuid():N}.txt";
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        await File.WriteAllTextAsync(filePath, "local");
        try
        {
            await using var node = new FileReadNode();
            var results = Sink(node.Output);

            await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest { Path = fileName }));

            (await results.ReceiveAsync().WaitAsync(TestTimeout)).Payload.Content.ShouldBe("local");
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task FileRead_DefaultsMaxBytesToSixteenMebibytes()
    {
        using var directory = TempDirectory.Create("read");
        var filePath = Path.Combine(directory.Path, "large.bin");
        await using (var stream = File.Create(filePath))
        {
            stream.SetLength(FileReadOptions.DefaultMaxBytes + 1);
        }

        await using var node = new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest
        {
            Path = "large.bin",
            ReadAs = FileReadMode.Bytes
        }));

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.FileReadTooLarge);
    }

    [Fact]
    public async Task FileRead_ExplicitNullMaxBytesKeepsUnlimitedReads()
    {
        using var directory = TempDirectory.Create("read");
        var filePath = Path.Combine(directory.Path, "large.bin");
        await using (var stream = File.Create(filePath))
        {
            stream.SetLength(FileReadOptions.DefaultMaxBytes + 1);
        }

        await using var node = new FileReadNode(new FileReadOptions
        {
            BaseDirectory = directory.Path,
            MaxBytes = null
        });
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest
        {
            Path = "large.bin",
            ReadAs = FileReadMode.Bytes
        }));

        (await results.ReceiveAsync().WaitAsync(TestTimeout)).Payload.BytesRead
            .ShouldBe(FileReadOptions.DefaultMaxBytes + 1);
    }

    [Fact]
    public async Task FileRead_UsesRequestEncoding()
    {
        using var directory = TempDirectory.Create("read");
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "encoded.txt"),
            Encoding.Unicode.GetBytes("A"));
        await using var node = new FileReadNode(new FileReadOptions
        {
            BaseDirectory = directory.Path,
            DefaultEncoding = "utf-8"
        });
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest
        {
            Path = "encoded.txt",
            Encoding = "utf-16"
        }));

        var result = (await results.ReceiveAsync().WaitAsync(TestTimeout)).Payload;
        result.Content.ShouldBe("A");
        result.BytesRead.ShouldBe(2);
        result.Encoding.ShouldBe("utf-16");
    }

    [Fact]
    public async Task FileRead_ReportsUnsupportedEncoding()
    {
        using var directory = TempDirectory.Create("read");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "encoded.txt"), "A");
        await using var node = new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest
        {
            Path = "encoded.txt",
            Encoding = "not-a-real-encoding"
        }));

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.FileReadUnsupportedEncoding);
    }

    [Fact]
    public async Task FileRead_RejectsFilesOverMaxBytes()
    {
        using var directory = TempDirectory.Create("read");
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "large.bin"), [1, 2, 3, 4]);
        await using var node = new FileReadNode(new FileReadOptions
        {
            BaseDirectory = directory.Path,
            MaxBytes = 3
        });
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest
        {
            Path = "large.bin",
            ReadAs = FileReadMode.Bytes
        }));

        (await errors.ReceiveAsync().WaitAsync(TestTimeout)).Code
            .ShouldBe(FileSystemErrorCodes.FileReadTooLarge);
    }

    [Fact]
    public async Task FileRead_SuccessEmitsEventCarryingCorrelationId()
    {
        using var directory = TempDirectory.Create("read");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "diag.txt"), "value");
        await using var node = new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        Sink(node.Output);
        var events = Sink(node.Events);

        var request = FlowMessage.Create(new FileReadRequest { Path = "diag.txt" });
        await node.Input.SendAsync(request);

        var @event = await events.ReceiveAsync().WaitAsync(TestTimeout);
        @event.Name.ShouldBe(FileReadNode.ReadSucceeded);
        @event.CorrelationId.ShouldBe(request.CorrelationId);
        @event.Attributes["bytesRead"].ShouldBe(5L);
    }

    [Fact]
    public async Task FileRead_CompletesOutput()
    {
        using var directory = TempDirectory.Create("read");
        await using var node = new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        var results = Sink(node.Output);

        node.Complete();
        await node.Completion.WaitAsync(TestTimeout);
        await results.Completion.WaitAsync(TestTimeout);
        results.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void FileRead_RejectsInvalidBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new FileReadNode(new FileReadOptions { BoundedCapacity = 0 }));

    [Fact]
    public void FileRead_RejectsUnsupportedDefaultEncoding()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new FileReadNode(new FileReadOptions { DefaultEncoding = "not-a-real-encoding" }));
        exception.Message.ShouldContain("defaultEncoding");
    }

    [Fact]
    public void FileRead_RejectsInvalidMaxBytes()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new FileReadNode(new FileReadOptions { MaxBytes = 0 }));
}
