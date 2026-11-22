using FluxFlow.Studio.UI.Features.Designer.Projects;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class CompiledPublishHostTests
{
    [Fact]
    public async Task Original_publish_host_handles_the_default_target_only()
    {
        var host = new OriginalPublishHost();
        IStudioFileAccess files = host;
        await files.PublishCompiledApplicationAsync("unsaved", 11);
        await Should.ThrowAsync<NotSupportedException>(() => files.PublishCompiledApplicationAsync("other", 10));
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
