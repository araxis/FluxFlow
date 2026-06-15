using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class DirectoryEnumerateNodeTests
{
    [Fact]
    public async Task DirectoryEnumerate_EmitsMatchingFilesInsideBaseDirectory()
    {
        using var directory = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "nested"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "nested", "child.txt"), "child");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "skip.bin"), "skip");
        var runtimeNode = CreateNode(new
        {
            directory = ".",
            baseDirectory = directory.Path,
            filter = "*.txt",
            includeSubdirectories = true,
            boundedCapacity = 8
        });
        var output = new BufferBlock<DirectoryEnumerateEntry>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var entries = await DrainUntilCompletedAsync(output);
        entries.Select(entry => entry.Name)
            .Order()
            .ShouldBe(["child.txt", "root.txt"]);
        entries.ShouldAllBe(entry => entry.EntryType == DirectoryEntryType.File);
        entries.ShouldAllBe(entry => entry.Directory == Path.GetFullPath(directory.Path));
        entries.Single(entry => entry.Name == "root.txt").Length.ShouldBe(4);
    }

    [Fact]
    public async Task DirectoryEnumerate_UsesConfiguredClockForEntryTimestamp()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "one.txt"), "one");
        var enumeratedAt = DateTimeOffset.Parse("2026-06-02T12:20:00Z");
        var runtimeNode = CreateNode(
            new
            {
                directory = ".",
                baseDirectory = directory.Path
            },
            options => options.UseClock(new FakeTimeProvider(enumeratedAt)));
        var output = new BufferBlock<DirectoryEnumerateEntry>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var entry = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        entry.EnumeratedAt.ShouldBe(enumeratedAt);
    }

    [Fact]
    public async Task DirectoryEnumerate_CanEmitDirectories()
    {
        using var directory = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "nested"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "root.txt"), "root");
        var runtimeNode = CreateNode(new
        {
            directory = ".",
            baseDirectory = directory.Path,
            includeFiles = false,
            includeDirectories = true,
            boundedCapacity = 4
        });
        var output = new BufferBlock<DirectoryEnumerateEntry>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var entry = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        entry.Name.ShouldBe("nested");
        entry.EntryType.ShouldBe(DirectoryEntryType.Directory);
        entry.Length.ShouldBeNull();
    }

    [Fact]
    public async Task DirectoryEnumerate_MaxEntriesLimitsOutput()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "two.txt"), "two");
        var runtimeNode = CreateNode(new
        {
            directory = ".",
            baseDirectory = directory.Path,
            maxEntries = 1
        });
        var output = new BufferBlock<DirectoryEnumerateEntry>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var entries = await DrainUntilCompletedAsync(output);
        entries.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DirectoryEnumerate_EmitsDiagnostics()
    {
        using var directory = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "diag.txt"), "value");
        var runtimeNode = CreateNode(new
        {
            directory = ".",
            baseDirectory = directory.Path
        });
        var output = new BufferBlock<DirectoryEnumerateEntry>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        LinkOutput(runtimeNode, output);
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var diagnosticNames = (await DrainDiagnosticsUntilCompletedAsync(diagnostics))
            .Select(diagnostic => diagnostic.Name)
            .ToArray();
        diagnosticNames.ShouldContain(FileSystemDiagnosticNames.DirectoryEnumerateStarted);
        diagnosticNames.ShouldContain(FileSystemDiagnosticNames.DirectoryEnumerateEntry);
        diagnosticNames.ShouldContain(FileSystemDiagnosticNames.DirectoryEnumerateCompleted);
    }

    [Fact]
    public async Task DirectoryEnumerate_MissingDirectoryFailsStartup()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new
        {
            directory = "missing",
            baseDirectory = directory.Path
        });
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => runtimeNode.Node.StartAsync());

        exception.Message.ShouldContain("failed to start");
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.DirectoryEnumerateDirectoryMissing);

        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DirectoryEnumerate_RejectsAbsoluteDirectoryWhenDisabled()
    {
        using var directory = TempDirectory.Create();
        var runtimeNode = CreateNode(new
        {
            directory = directory.Path,
            baseDirectory = directory.Path
        });
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => runtimeNode.Node.StartAsync());

        exception.Message.ShouldContain("failed to start");
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(FileSystemErrorCodes.DirectoryEnumerateAbsolutePathDenied);

        runtimeNode.Node.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void DirectoryEnumerate_RejectsMissingDirectoryOption()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { }));

        exception.Message.ShouldContain("directory");
    }

    [Fact]
    public void DirectoryEnumerate_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { directory = ".", boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void DirectoryEnumerate_RejectsDisabledEntryTypes()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                directory = ".",
                includeFiles = false,
                includeDirectories = false
            }));

        exception.Message.ShouldContain("includeFiles");
    }

    [Fact]
    public void DirectoryEnumerate_RejectsInvalidMaxEntries()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { directory = ".", maxEntries = 0 }));

        exception.Message.ShouldContain("maxEntries");
    }

    private static RuntimeNode CreateNode(
        object configuration,
        Action<FileSystemComponentOptions>? configure = null)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterFileSystemComponents(configure ?? (_ => { }));
        registry.TryGetFactory(FileSystemComponentTypes.DirectoryEnumerate, out var factory).ShouldBeTrue();
        return factory(FileSystemTestHost.CreateContext(
            FileSystemComponentTypes.DirectoryEnumerate,
            configuration,
            "enumerate"));
    }

    private static void LinkOutput(RuntimeNode runtimeNode, BufferBlock<DirectoryEnumerateEntry> target)
    {
        runtimeNode.FindOutput(new PortName(FileSystemComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<DirectoryEnumerateEntry>(
                    new PortAddress("test", new NodeName("entries"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private static async Task<List<DirectoryEnumerateEntry>> DrainUntilCompletedAsync(
        BufferBlock<DirectoryEnumerateEntry> output)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entries = new List<DirectoryEnumerateEntry>();
        while (await output.OutputAvailableAsync(cancellation.Token))
        {
            while (output.TryReceive(out var entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static async Task<List<FlowDiagnostic>> DrainDiagnosticsUntilCompletedAsync(
        BufferBlock<FlowDiagnostic> diagnostics)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entries = new List<FlowDiagnostic>();
        while (await diagnostics.OutputAvailableAsync(cancellation.Token))
        {
            while (diagnostics.TryReceive(out var entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
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
                $"fluxflow-directory-enumerate-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Path))
            {
                System.IO.Directory.Delete(Path, recursive: true);
            }
        }
    }
}
