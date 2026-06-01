using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class FileSystemComponentModuleTests
{
    [Fact]
    public void RegisterFileSystemComponents_AddsFileFactories()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterFileSystemComponents();

        registry.TryGetFactory(FileSystemComponentTypes.FileRead, out var readFactory).ShouldBeTrue();
        readFactory.ShouldNotBeNull();
        registry.TryGetFactory(FileSystemComponentTypes.FileWatch, out var watchFactory).ShouldBeTrue();
        watchFactory.ShouldNotBeNull();
        registry.TryGetFactory(FileSystemComponentTypes.FileWrite, out var writeFactory).ShouldBeTrue();
        writeFactory.ShouldNotBeNull();
    }
}
