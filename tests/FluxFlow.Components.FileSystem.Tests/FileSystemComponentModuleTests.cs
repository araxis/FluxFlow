using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class FileSystemComponentModuleTests
{
    [Fact]
    public void RegisterFileSystemComponents_AddsFileWriteFactory()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterFileSystemComponents();

        registry.TryGetFactory(FileSystemComponentTypes.FileWrite, out var factory).ShouldBeTrue();
        factory.ShouldNotBeNull();
    }
}
