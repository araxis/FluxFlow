using FluxFlow.Components.Resources.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Resources.Tests;

public sealed class ResourceDescriptorCatalogTests
{
    [Fact]
    public async Task Lookup_returns_descriptor_for_matching_reference()
    {
        var descriptor = CreateDescriptor("primary-profile", "profile");
        var catalog = new ResourceDescriptorCatalog([descriptor]);

        var result = await catalog.LookupAsync(new ResourceReference
        {
            Name = new ResourceName("primary-profile"),
            Kind = "profile"
        });

        result.Found.ShouldBeTrue();
        result.Descriptor.ShouldBeSameAs(descriptor);
        result.Diagnostic.ShouldBeNull();
    }

    [Fact]
    public async Task Lookup_returns_missing_diagnostic_for_unknown_reference()
    {
        var catalog = new ResourceDescriptorCatalog([CreateDescriptor("primary-profile", "profile")]);

        var result = await catalog.LookupAsync(new ResourceReference
        {
            Name = new ResourceName("secondary-profile"),
            Kind = "profile"
        });

        result.Found.ShouldBeFalse();
        result.Diagnostic.ShouldNotBeNull();
        result.Diagnostic.Code.ShouldBe(ResourceDiagnosticCode.MissingResource);
        result.Diagnostic.Severity.ShouldBe(ResourceDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Lookup_returns_kind_mismatch_diagnostic()
    {
        var catalog = new ResourceDescriptorCatalog([CreateDescriptor("primary", "profile")]);

        var result = await catalog.LookupAsync(new ResourceReference
        {
            Name = new ResourceName("primary"),
            Kind = "credential"
        });

        result.Found.ShouldBeFalse();
        result.Descriptor.ShouldNotBeNull();
        result.Diagnostic.ShouldNotBeNull();
        result.Diagnostic.Code.ShouldBe(ResourceDiagnosticCode.KindMismatch);
    }

    [Fact]
    public void Duplicate_helper_reports_duplicate_names()
    {
        var diagnostics = ResourceDiagnostics.FindDuplicateResources(
        [
            CreateDescriptor("primary", "profile"),
            CreateDescriptor("primary", "credential")
        ]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Code.ShouldBe(ResourceDiagnosticCode.DuplicateResource);
        diagnostics[0].Name.ShouldBe(new ResourceName("primary"));
    }

    [Fact]
    public void Catalog_rejects_duplicate_descriptors()
    {
        var act = () => new ResourceDescriptorCatalog(
        [
            CreateDescriptor("primary", "profile"),
            CreateDescriptor("primary", "profile")
        ]);

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain(nameof(ResourceDiagnosticCode.DuplicateResource));
    }

    [Fact]
    public async Task Missing_helper_reports_missing_and_kind_mismatch_references()
    {
        var catalog = new ResourceDescriptorCatalog([CreateDescriptor("primary", "profile")]);

        var diagnostics = await ResourceDiagnostics.FindMissingResourcesAsync(
            catalog,
            [
                new ResourceReference { Name = new ResourceName("primary"), Kind = "profile" },
                new ResourceReference { Name = new ResourceName("primary"), Kind = "credential" },
                new ResourceReference { Name = new ResourceName("secondary"), Kind = "profile" }
            ]);

        diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe(
        [
            ResourceDiagnosticCode.KindMismatch,
            ResourceDiagnosticCode.MissingResource
        ]);
    }

    [Fact]
    public void Unused_helper_reports_unreferenced_descriptors()
    {
        var diagnostics = ResourceDiagnostics.FindUnusedResources(
            [
                CreateDescriptor("primary", "profile"),
                CreateDescriptor("secondary", "profile")
            ],
            [
                new ResourceReference { Name = new ResourceName("primary"), Kind = "profile" }
            ]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Code.ShouldBe(ResourceDiagnosticCode.UnusedResource);
        diagnostics[0].Name.ShouldBe(new ResourceName("secondary"));
    }

    [Fact]
    public void Descriptor_validation_reports_default_name_and_empty_metadata()
    {
        var diagnostics = ResourceDiagnostics.ValidateDescriptors(
        [
            new ResourceDescriptor
            {
                Name = default,
                Kind = " ",
                Metadata = new Dictionary<string, string>
                {
                    [""] = "value",
                    ["empty"] = ""
                }
            }
        ]);

        diagnostics.ShouldContain(diagnostic => diagnostic.Code == ResourceDiagnosticCode.InvalidResource);
        diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains("name", StringComparison.Ordinal));
        diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains("Keys", StringComparison.Ordinal));
        diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains("Values", StringComparison.Ordinal));
    }

    [Fact]
    public void Reference_attributes_are_preserved()
    {
        var reference = new ResourceReference
        {
            Name = new ResourceName("primary"),
            Kind = "profile",
            Attributes = new Dictionary<string, string>
            {
                ["scope"] = "runtime"
            }
        };

        reference.Attributes["scope"].ShouldBe("runtime");
    }

    private static ResourceDescriptor CreateDescriptor(string name, string kind) => new()
    {
        Name = new ResourceName(name),
        Kind = kind,
        DisplayName = "Primary",
        Summary = "Reusable profile.",
        Metadata = new Dictionary<string, string>
        {
            ["owner"] = "runtime"
        }
    };
}
