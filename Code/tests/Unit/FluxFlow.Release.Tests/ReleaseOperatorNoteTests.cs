using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class ReleaseOperatorNoteTests
{
    [Fact]
    public void Release_operator_note_documents_guarded_command_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var notePath = Path.Combine(root, "..", "docs", "release-operations.md");
        var indexPath = Path.Combine(root, "..", "docs", "README.md");

        File.Exists(notePath).ShouldBeTrue("release instructions should be part of the product documentation.");

        var note = File.ReadAllText(notePath);
        note.ShouldContain("./eng/list-package-releases.ps1");
        note.ShouldContain("./eng/package-release-preflight.ps1 -Package core");
        note.ShouldContain("./eng/package-release-dry-run.ps1 -Package core");
        note.ShouldContain("./eng/package-release-tag.ps1 -Package core");
        note.ShouldContain("./eng/package-release-tag.ps1 -Package core -Push");
        note.ShouldContain("Do not create release tags directly.");

        var index = File.ReadAllText(indexPath);
        index.ShouldContain("release-operations.md");
    }
}

