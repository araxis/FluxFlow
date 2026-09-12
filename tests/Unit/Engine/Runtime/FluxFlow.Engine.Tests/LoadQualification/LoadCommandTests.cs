using FluxFlow.Engine.Benchmarks.LoadQualification;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests.LoadQualification;

public sealed class LoadCommandTests
{
    [Fact]
    public async Task Load_command_preserves_an_existing_report()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "original evidence");
            await Should.ThrowAsync<IOException>(() => LoadCommand.RunAsync(["--output", path]));
            (await File.ReadAllTextAsync(path)).ShouldBe("original evidence");
        }
        finally { File.Delete(path); }
    }
}
