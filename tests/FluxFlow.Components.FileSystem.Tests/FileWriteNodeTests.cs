using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class FileWriteNodeTests
{
    [Fact]
    public async Task FileWrite_WritesStringContentInsideBaseDirectory()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new
        {
            baseDirectory = directory.Path,
            boundedCapacity = 4
        });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileWriteRequest>>();
        var results = new BufferBlock<FileWriteResult>();
        LinkResult(runtimeNode, results);

        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = "logs/output.txt",
            Content = "hello"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var expectedPath = Path.Combine(directory.Path, "logs", "output.txt");
        var text = await File.ReadAllTextAsync(expectedPath);
        text.ShouldBe("hello");
        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Path.ShouldBe(Path.GetFullPath(expectedPath));
        result.BytesWritten.ShouldBe(5);
        result.Mode.ShouldBe(FileWriteMode.Overwrite);
    }

    [Fact]
    public async Task FileWrite_WritesBytesAndAppendsInOrder()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileWriteRequest>>();
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = "data.bin",
            Bytes = [1, 2],
            Mode = FileWriteMode.Overwrite
        });
        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = "data.bin",
            Bytes = [3, 4],
            Mode = FileWriteMode.Append
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        File.ReadAllBytes(Path.Combine(directory.Path, "data.bin")).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public async Task FileWrite_CreateNewReportsIoFailureAndContinues()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "target.txt"), "existing");
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileWriteRequest>>();
        var errors = new BufferBlock<FlowError>();
        var results = new BufferBlock<FileWriteResult>();
        runtimeNode.Node.Errors.LinkTo(errors);
        LinkResult(runtimeNode, results);

        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = "target.txt",
            Content = "first",
            Mode = FileWriteMode.CreateNew
        });
        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = "next.txt",
            Content = "second",
            Mode = FileWriteMode.CreateNew
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileWriteIoFailed);
        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Path.ShouldEndWith("next.txt");
        var text = await File.ReadAllTextAsync(Path.Combine(directory.Path, "next.txt"));
        text.ShouldBe("second");
    }

    [Fact]
    public async Task FileWrite_RejectsAbsolutePathWhenDisabled()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileWriteRequest>>();
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = Path.Combine(directory.Path, "blocked.txt"),
            Content = "blocked"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileWriteAbsolutePathDenied);
        File.Exists(Path.Combine(directory.Path, "blocked.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task FileWrite_RejectsRelativePathThatEscapesBaseDirectory()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileWriteRequest>>();
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = "../outside.txt",
            Content = "blocked"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileWriteInvalidPath);
        error.Message.ShouldContain("baseDirectory");
    }

    [Fact]
    public async Task FileWrite_UsesRequestEncoding()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path, defaultEncoding = "utf-8" });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileWriteRequest>>();
        var results = new BufferBlock<FileWriteResult>();
        LinkResult(runtimeNode, results);

        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = "encoded.txt",
            Content = "A",
            Encoding = "utf-16"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.BytesWritten.ShouldBe(2);
        File.ReadAllBytes(Path.Combine(directory.Path, "encoded.txt"))
            .ShouldBe(Encoding.Unicode.GetBytes("A"));
    }

    [Fact]
    public async Task FileWrite_ReportsUnsupportedEncoding()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileWriteRequest>>();
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = "encoded.txt",
            Content = "A",
            Encoding = "not-a-real-encoding"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileWriteUnsupportedEncoding);
    }

    [Fact]
    public async Task FileWrite_ReportsMissingContent()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileWriteRequest>>();
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = "empty.txt"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.FileWriteContentMissing);
    }

    [Fact]
    public async Task FileWrite_EmitsDiagnostics()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileWriteRequest>>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!.LinkToDiscard();

        await input.Target.SendAsync(new FileWriteRequest
        {
            Path = "diag.txt",
            Content = "value"
        });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(FileSystemDiagnosticNames.FileWriteSucceeded);
        diagnostic.Attributes["bytesWritten"].ShouldBe(5);
    }

    [Fact]
    public async Task FileWrite_CompletesResultOutput()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new { baseDirectory = directory.Path });
        var input = runtimeNode.FindInput(new PortName(FileSystemComponentPorts.Input))
            .ShouldBeOfType<InputPort<FileWriteRequest>>();
        var results = new BufferBlock<FileWriteResult>();
        LinkResult(runtimeNode, results);

        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        results.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public void FileWrite_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void FileWrite_RejectsUnsupportedDefaultEncoding()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { defaultEncoding = "not-a-real-encoding" }));

        exception.Message.ShouldContain("defaultEncoding");
    }

    private static RuntimeNode CreateNode(object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterFileSystemComponents();
        registry.TryGetFactory(FileSystemComponentTypes.FileWrite, out var factory).ShouldBeTrue();
        return factory(FileSystemTestHost.CreateContext(configuration));
    }

    private static void LinkResult(RuntimeNode runtimeNode, BufferBlock<FileWriteResult> target)
    {
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<FileWriteResult>(
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
                $"fluxflow-filesystem-{Guid.NewGuid():N}");
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
