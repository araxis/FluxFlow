using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class PackagePublishWorkflowTests
{
    [Fact]
    public void Publish_workflow_uses_explicit_suites_before_provider_and_publication_gates()
    {
        var workflow = NormalizeLineEndings(ReadWorkflow());
        var previousIndex = RequiredIndexOf(workflow, "- name: Build");
        var providerIndex = RequiredIndexOf(workflow, "- name: Validate binary compatibility and pack");
        var commands = Regex.Matches(workflow,
            @"(?m)^\s+run:\s+\./eng/test\.ps1 -Suite (?<suite>\w+) -Configuration Release -NoBuild\s*$");

        commands.Select(match => match.Groups["suite"].Value)
            .ShouldBe(["Unit"]);
        foreach (Match command in commands)
        {
            command.Index.ShouldBeGreaterThan(previousIndex);
            command.Index.ShouldBeLessThan(providerIndex);
            var start = workflow.LastIndexOf("- name:", command.Index, StringComparison.Ordinal);
            var step = workflow[start..(command.Index + command.Length)];
            step.ShouldContain("shell: pwsh");
            step.ShouldNotContain("continue-on-error");
            step.ShouldNotContain("if:");
            previousIndex = command.Index;
        }

        workflow.ShouldNotContain("dotnet test FluxFlow.sln");
        workflow.ShouldNotContain("-Suite Smoke");
        workflow.ShouldNotContain("-Suite All");
    }

    [Fact]
    public void Publish_workflow_invoked_repository_scripts_exist()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var commands = Regex.Matches(
            ReadWorkflow(),
            @"(?m)^\s*(?:run:\s*)?\./(?<path>[A-Za-z0-9_./-]+\.ps1)(?=\s|$)");

        commands.Count.ShouldBeGreaterThan(0, "Expected repository script invocations in the workflow.");
        foreach (Match command in commands)
        {
            var relativePath = command.Groups["path"].Value;
            File.Exists(Path.Combine(root, relativePath)).ShouldBeTrue(
                $"Publishing invokes missing repository script '{relativePath}'.");
        }
    }

    [Fact]
    public void Publish_workflow_orders_build_compatibility_integrity_and_publication_gates()
    {
        var workflow = ReadWorkflow();

        var restoreIndex = RequiredIndexOf(workflow, "- name: Restore");
        var buildIndex = RequiredIndexOf(workflow, "- name: Build");
        var testIndex = RequiredIndexOf(workflow, "- name: Test");
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
        compatibilityIndex.ShouldBeGreaterThan(testIndex);
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
    public void Publish_workflow_uses_exact_release_environment_and_trusted_publishing_permissions()
    {
        var workflow = NormalizeLineEndings(ReadWorkflow());
        var permissionsIndex = RequiredIndexOf(workflow, "permissions:\n");
        var jobsIndex = RequiredIndexOf(workflow, "jobs:\n");
        var stepsIndex = RequiredIndexOf(workflow, "\n    steps:\n");
        var permissions = workflow[permissionsIndex..jobsIndex];
        var releaseJobHeader = workflow[jobsIndex..stepsIndex];

        CountOccurrences(workflow, "permissions:").ShouldBe(1);
        CountOccurrences(workflow, "contents: write").ShouldBe(1);
        CountOccurrences(workflow, "id-token: write").ShouldBe(1);
        CountOccurrences(permissions, "\n  contents: write\n").ShouldBe(1);
        CountOccurrences(permissions, "\n  id-token: write\n").ShouldBe(1);
        CountOccurrences(workflow, "environment:").ShouldBe(1);
        CountOccurrences(releaseJobHeader, "\n    environment: release\n").ShouldBe(1);
    }

    [Fact]
    public void Publish_workflow_logs_into_package_feed_immediately_before_publication_without_static_api_key_secret()
    {
        var workflow = NormalizeLineEndings(ReadWorkflow());
        const string loginName = "- name: Package feed login";
        const string publishName = "- name: Publish package";
        var loginIndex = RequiredIndexOf(workflow, loginName);
        var publishIndex = RequiredIndexOf(workflow, publishName);
        var verificationIndex = RequiredIndexOf(workflow, "- name: Verify package feed");
        var nextNamedStepIndex = workflow.IndexOf(
            "- name:",
            loginIndex + loginName.Length,
            StringComparison.Ordinal);
        var loginStep = workflow[loginIndex..publishIndex];
        var publishStep = workflow[publishIndex..verificationIndex];

        CountOccurrences(workflow, loginName).ShouldBe(1);
        CountOccurrences(workflow, publishName).ShouldBe(1);
        CountOccurrences(workflow, "uses: NuGet/login@v1").ShouldBe(1);
        nextNamedStepIndex.ShouldBe(publishIndex);
        CountOccurrences(loginStep, "\n      - ").ShouldBe(0);
        CountOccurrences(loginStep, "id: nuget-login").ShouldBe(1);
        CountOccurrences(loginStep, "uses: NuGet/login@v1").ShouldBe(1);
        CountOccurrences(loginStep, "user: ${{ secrets.NUGET_USER }}").ShouldBe(1);
        CountOccurrences(
            publishStep,
            "--api-key \"${{ steps.nuget-login.outputs.NUGET_API_KEY }}\"").ShouldBe(1);
        CountOccurrences(
            workflow,
            "secrets.NUGET_API_KEY",
            StringComparison.OrdinalIgnoreCase).ShouldBe(0);
        CountOccurrences(
            workflow,
            "$env:NUGET_API_KEY",
            StringComparison.OrdinalIgnoreCase).ShouldBe(0);
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

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

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
