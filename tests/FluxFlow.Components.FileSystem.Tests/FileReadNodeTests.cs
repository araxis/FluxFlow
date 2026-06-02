using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class FileReadNodeTests
{
    [Fact]
    public async Task FileRead_ReadsTextInsideBaseDirectory()
    {
        using var directory = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "logs"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "logs", "input.txt"), "hello");
        var runtimeNode = CreateNode(new
        {
            baseDirectory = directory.Path,
            boundedCapacity = 4
        });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var results = new BufferBlock<FileReadResult>();
        LinkResult(runtimeNode, results);

        await input.Target.SendAsync(new FileReadRequest
        {
            Path = "logs/input.txt"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var expectedPath = Path.Combine(directory.Path, "logs", "input.txt");
        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Path.ShouldBe(Path.GetFullPath(expectedPath));
        result.Content.ShouldBe("hello");
        result.Bytes.ShouldBeNull();
        result.BytesRead.ShouldBe(5);
        result.ReadAs.ShouldBe(FileReadMode.Text);
        result.Encoding.ShouldBe("utf-8");
    }

    [Fact]
    public async Task FileRead_UsesConfiguredClockForResultTimestamp()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "input.txt"), "hello");
        var readAt = DateTimeOffset.Parse("2026-06-02T12:10:00Z");
        var runtimeNode = CreateNode(
            new { baseDirectory = directory.Path },
            options => options.UseClock(new RecordingFileSystemClock(readAt)));
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var results = new BufferBlock<FileReadResult>();
        LinkResult(runtimeNode, results);

        await input.Target.SendAsync(new FileReadRequest { Path = "input.txt" });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.ReadAt.ShouldBe(readAt);
    }

    [Fact]
    public async Task FileRead_ReadsBytes()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "data.bin"), [1, 2, 3]);
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var results = new BufferBlock<FileReadResult>();
        LinkResult(runtimeNode, results);

        await input.Target.SendAsync(new FileReadRequest
        {
            Path = "data.bin",
            ReadAs = FileReadMode.Bytes
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Bytes.ShouldBe([1, 2, 3]);
        result.Content.ShouldBeNull();
        result.Encoding.ShouldBeNull();
        result.BytesRead.ShouldBe(3);
        result.ReadAs.ShouldBe(FileReadMode.Bytes);
    }

    [Fact]
    public async Task FileRead_MissingFileReportsErrorAndContinues()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "next.txt"), "second");
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var errors = new BufferBlock<FlowError>();
        var results = new BufferBlock<FileReadResult>();
        runtimeNode.Node.Errors.LinkTo(errors);
        LinkResult(runtimeNode, results);

        await input.Target.SendAsync(new FileReadRequest { Path = "missing.txt" });
        await input.Target.SendAsync(new FileReadRequest { Path = "next.txt" });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileReadNotFound);
        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Content.ShouldBe("second");
    }

    [Fact]
    public async Task FileRead_RejectsAbsolutePathWhenDisabled()
    {
        using var directory = TempDirectory.Create();
        var filePath = Path.Combine(directory.Path, "blocked.txt");
        await File.WriteAllTextAsync(filePath, "blocked");
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileReadRequest { Path = filePath });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileReadAbsolutePathDenied);
    }

    [Fact]
    public async Task FileRead_RejectsRelativePathThatEscapesBaseDirectory()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileReadRequest { Path = "../outside.txt" });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileReadInvalidPath);
        error.Message.ShouldContain("baseDirectory");
    }

    [Fact]
    public async Task FileRead_UsesRequestEncoding()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "encoded.txt"),
            Encoding.Unicode.GetBytes("A"));
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path, defaultEncoding = "utf-8" });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var results = new BufferBlock<FileReadResult>();
        LinkResult(runtimeNode, results);

        await input.Target.SendAsync(new FileReadRequest
        {
            Path = "encoded.txt",
            Encoding = "utf-16"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Content.ShouldBe("A");
        result.BytesRead.ShouldBe(2);
        result.Encoding.ShouldBe("utf-16");
    }

    [Fact]
    public async Task FileRead_ReportsUnsupportedEncoding()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "encoded.txt"), "A");
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileReadRequest
        {
            Path = "encoded.txt",
            Encoding = "not-a-real-encoding"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileReadUnsupportedEncoding);
    }

    [Fact]
    public async Task FileRead_RejectsFilesOverMaxBytes()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "large.bin"), [1, 2, 3, 4]);
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path, maxBytes = 3 });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileReadRequest
        {
            Path = "large.bin",
            ReadAs = FileReadMode.Bytes
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileReadTooLarge);
    }

    [Fact]
    public async Task FileRead_EmitsDiagnostics()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "diag.txt"), "value");
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileReadRequest { Path = "diag.txt" });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(FileSystemDiagnosticNames.FileReadSucceeded);
        diagnostic.Attributes["bytesRead"].ShouldBe(5L);
    }

    [Fact]
    public async Task FileRead_CompletesResultOutput()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileReadRequest>>();
        var results = new BufferBlock<FileReadResult>();
        LinkResult(runtimeNode, results);

        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        await results.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        results.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void FileRead_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void FileRead_RejectsUnsupportedDefaultEncoding()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { defaultEncoding = "not-a-real-encoding" }));

        exception.Message.ShouldContain("defaultEncoding");
    }

    [Fact]
    public void FileRead_RejectsInvalidMaxBytes()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { maxBytes = 0 }));

        exception.Message.ShouldContain("maxBytes");
    }

    private static RuntimeNode CreateNode(
        object configuration,
        Action<FileSystemComponentOptions>? configure = null)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterFileSystemComponents(configure ?? (_ => { }));
        registry.TryGetFactory(FileSystemComponentTypes.FileRead, out var factory).ShouldBeTrue();
        return factory(FileSystemTestHost.CreateContext(
            FileSystemComponentTypes.FileRead,
            configuration,
            "reader"));
    }

    private static void LinkResult(RuntimeNode runtimeNode, BufferBlock<FileReadResult> target)
    {
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<FileReadResult>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fluxflow-filesystem-read-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
