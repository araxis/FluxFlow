using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackagePublishWorkflowTests
{
    [Fact]
    public void Publish_workflow_orders_build_compatibility_integrity_and_publication_gates()
    {
        var workflow = ReadWorkflow();

        var restoreIndex = RequiredIndexOf(workflow, "- name: Restore");
        var buildIndex = RequiredIndexOf(workflow, "- name: Build");
        var testIndex = RequiredIndexOf(workflow, "- name: Test");
        var inputProviderIndex = RequiredIndexOf(workflow, "Validate durable input provider");
        var outputProviderIndex = RequiredIndexOf(workflow, "Validate durable output provider");
        var compatibilityIndex = RequiredIndexOf(workflow, "./eng/package-binary-compat-preflight.ps1");
        var archiveIndex = RequiredIndexOf(workflow, "Inspect package archive");
        var smokeIndex = RequiredIndexOf(workflow, "Smoke package consumer");
        var notesIndex = RequiredIndexOf(workflow, "Prepare release notes");
        var uploadIndex = RequiredIndexOf(workflow, "Upload workflow package artifacts");
        var collisionCheckIndex = RequiredIndexOf(workflow, "Require unpublished package version");
        var publishIndex = RequiredIndexOf(workflow, "Publish package");
        var verificationIndex = RequiredIndexOf(workflow, "Verify package feed");
        var releaseIndex = RequiredIndexOf(workflow, "Create release");

        buildIndex.ShouldBeGreaterThan(restoreIndex);
        testIndex.ShouldBeGreaterThan(buildIndex);
        inputProviderIndex.ShouldBeGreaterThan(testIndex);
        outputProviderIndex.ShouldBeGreaterThan(inputProviderIndex);
        compatibilityIndex.ShouldBeGreaterThan(outputProviderIndex);
        archiveIndex.ShouldBeGreaterThan(compatibilityIndex);
        smokeIndex.ShouldBeGreaterThan(archiveIndex);
        notesIndex.ShouldBeGreaterThan(smokeIndex);
        uploadIndex.ShouldBeGreaterThan(notesIndex);
        collisionCheckIndex.ShouldBeGreaterThan(uploadIndex);
        publishIndex.ShouldBeGreaterThan(collisionCheckIndex);
        verificationIndex.ShouldBeGreaterThan(publishIndex);
        releaseIndex.ShouldBeGreaterThan(verificationIndex);
    }

    [Fact]
    public void Publish_workflow_uses_resolved_binary_compatibility_gate_as_sole_pack_path()
    {
        var workflow = ReadWorkflow();
        var compatibilityIndex = RequiredIndexOf(workflow, "./eng/package-binary-compat-preflight.ps1");
        var archiveIndex = RequiredIndexOf(workflow, "- name: Inspect package archive");
        var compatibilityStep = workflow[compatibilityIndex..archiveIndex];

        CountOccurrences(workflow, "package-binary-compat-preflight.ps1").ShouldBe(1);
        CountOccurrences(workflow, "dotnet pack", StringComparison.OrdinalIgnoreCase).ShouldBe(0);
        workflow.ShouldContain("-EnvironmentPath $env:GITHUB_ENV");
        compatibilityStep.ShouldContain("-Package \"$env:PACKAGE_ALIAS\"");
        compatibilityStep.ShouldContain("-Version \"$env:PACKAGE_VERSION\"");
        compatibilityStep.ShouldContain("-BaselineVersion \"$env:PACKAGE_BINARY_COMPATIBILITY_BASELINE\"");
        compatibilityStep.ShouldContain("-PackageSource \"https://api.nuget.org/v3/index.json\"");
        compatibilityStep.ShouldContain("-OutputPath artifacts/packages");
    }

    [Fact]
    public void Publish_workflow_does_not_treat_duplicate_publication_as_success()
    {
        var workflow = ReadWorkflow();

        workflow.ShouldNotContain("--skip-duplicate");
        workflow.ShouldContain("package-release-availability.ps1");
        workflow.ShouldContain("-ExpectedState Missing");
    }

    private static string ReadWorkflow()
    {
        var repositoryRoot = ReleaseTestPaths.FindRepositoryRoot();
        var workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "publish-nuget.yml");

        File.Exists(workflowPath).ShouldBeTrue($"Expected workflow at '{workflowPath}'.");
        return File.ReadAllText(workflowPath);
    }

    private static int RequiredIndexOf(string text, string value)
    {
        var index = text.IndexOf(value, StringComparison.Ordinal);
        index.ShouldBeGreaterThanOrEqualTo(0, $"Expected workflow to contain '{value}'.");
        return index;
    }

    private static int CountOccurrences(
        string text,
        string value,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var count = 0;
        var startIndex = 0;

        while ((startIndex = text.IndexOf(value, startIndex, comparison)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }
}
