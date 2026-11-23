using FluxFlow.Studio.UI.Features.Designer.Projects;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class CompiledPublishHostTests
{
    [Theory]
    [InlineData(null, "App.exe")]
    [InlineData(".fluxflow", "App.exe")]
    [InlineData("Orders.fluxflow", "Orders.exe")]
    [InlineData("Orders.EU.fluxflow", "Orders.EU.exe")]
    [InlineData("application.fluxflow", "application.exe")]
    public void Compiled_filename_uses_project_identity(string? name, string expected)
    {
        var graph = StudioTestServices.CreateGraph();
        if (name is not null) graph.ProjectFile = new StudioFile(name, "");
        new CompiledPublishTarget().FileName(graph.CompiledFileStem).ShouldBe(expected);
        new CompiledPublishTarget(10, "linux-x64").FileName(graph.CompiledFileStem).ShouldBe(expected[..^4]);
    }

    [Fact]
    public async Task Original_publish_host_handles_the_default_target_only()
    {
        var host = new OriginalPublishHost();
        IStudioFileAccess files = host;
        await files.PublishCompiledApplicationAsync("unsaved", new CompiledPublishTarget(), "Orders.exe");
        await Should.ThrowAsync<NotSupportedException>(() => files.PublishCompiledApplicationAsync("other", new CompiledPublishTarget(11, "linux-x64"), "Orders"));
        await Should.ThrowAsync<NotSupportedException>(() => files.PublishCompiledApplicationAsync("other", 10, "Orders.exe"));
        host.Published.ShouldBe("unsaved");
    }

    private sealed class OriginalPublishHost : IStudioFileAccess
    {
        public string? Published { get; private set; }
        public Task PublishCompiledApplicationAsync(string configuration)
        {
            Published = configuration;
            return Task.CompletedTask;
        }

        public Task<StudioFile?> OpenAsync(string extension) => throw new NotSupportedException();
        public Task<StudioImageFile?> OpenImageAsync() => throw new NotSupportedException();
        public Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false) => throw new NotSupportedException();
    }
}
