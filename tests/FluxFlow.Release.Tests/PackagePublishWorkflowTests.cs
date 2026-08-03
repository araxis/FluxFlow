using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackagePublishWorkflowTests
{
    [Fact]
    public void Publish_workflow_orders_upload_collision_check_publish_verification_and_release_creation()
    {
        var workflow = ReadWorkflow();

        var uploadIndex = workflow.IndexOf("Upload workflow package artifacts", StringComparison.Ordinal);
        var collisionCheckIndex = workflow.IndexOf("Require unpublished package version", StringComparison.Ordinal);
        var publishIndex = workflow.IndexOf("Publish package", StringComparison.Ordinal);
        var verificationIndex = workflow.IndexOf("Verify package feed", StringComparison.Ordinal);
        var releaseIndex = workflow.IndexOf("Create release", StringComparison.Ordinal);

        uploadIndex.ShouldBeGreaterThanOrEqualTo(0);
        collisionCheckIndex.ShouldBeGreaterThan(uploadIndex);
        publishIndex.ShouldBeGreaterThan(collisionCheckIndex);
        verificationIndex.ShouldBeGreaterThan(publishIndex);
        releaseIndex.ShouldBeGreaterThan(verificationIndex);
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
}
