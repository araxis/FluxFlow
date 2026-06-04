using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class ReleaseOperatorNoteTests
{
    [Fact]
    public void Release_operator_note_documents_guarded_command_path()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var notePath = Path.Combine(root, "memory", "124-release-operator-note.md");
        var indexPath = Path.Combine(root, "memory", "00-index.md");

        File.Exists(notePath).ShouldBeTrue("operator note should exist in memory.");

        var note = File.ReadAllText(notePath);
        note.ShouldContain("./eng/list-package-releases.ps1");
        note.ShouldContain("./eng/package-release-dry-run.ps1 -Package components-configuration");
        note.ShouldContain("./eng/package-release-tag.ps1 -Package components-configuration");
        note.ShouldContain("./eng/package-release-tag.ps1 -Package components-configuration -Push");
        note.ShouldContain("Do not create release tags directly.");

        var index = File.ReadAllText(indexPath);
        index.ShouldContain("124-release-operator-note.md");
    }
}
