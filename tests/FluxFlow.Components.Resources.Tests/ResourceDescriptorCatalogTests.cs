using FluxFlow.Components.Resources.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Resources.Tests;

public sealed class ResourceDescriptorCatalogTests
{
    [Fact]
    public async Task Catalog_builder_creates_normalized_descriptor_and_lookup_catalog()
    {
        var catalog = new ResourceDescriptorCatalogBuilder()
            .Add(
                " primary-profile ",
                kind: " profile ",
                displayName: " Primary Profile ",
                summary: " Runtime profile. ",
                metadata: new Dictionary<string, string>
                {
                    [" owner "] = " runtime "
                })
            .BuildCatalog();

        var descriptor = catalog.GetResources().ShouldHaveSingleItem();
        descriptor.Name.ShouldBe(new ResourceName("primary-profile"));
        descriptor.Kind.ShouldBe("profile");
        descriptor.DisplayName.ShouldBe("Primary Profile");
        descriptor.Summary.ShouldBe("Runtime profile.");
        descriptor.Metadata["owner"].ShouldBe("runtime");

        var result = await catalog.LookupAsync(new ResourceReference
        {
            Name = new ResourceName("primary-profile"),
            Kind = "profile"
        });

        result.Found.ShouldBeTrue();
        result.Descriptor.ShouldBeSameAs(descriptor);
    }

    [Fact]
    public void Catalog_builder_accepts_existing_descriptors_and_snapshots_build_results()
    {
        var builder = new ResourceDescriptorCatalogBuilder()
            .Add(CreateDescriptor("primary", "profile"));

        var descriptors = builder.BuildDescriptors();
        builder.Add(CreateDescriptor("secondary", "profile"));

        descriptors.Count.ShouldBe(1);
        descriptors[0].Name.ShouldBe(new ResourceName("primary"));
        builder.BuildDescriptors().Select(descriptor => descriptor.Name).ShouldBe(
        [
            new ResourceName("primary"),
            new ResourceName("secondary")
        ]);
    }

    [Fact]
    public void Catalog_builder_add_range_preserves_order()
    {
        var descriptors = new ResourceDescriptorCatalogBuilder()
            .AddRange(
            [
                CreateDescriptor("first", "profile"),
                CreateDescriptor("second", "profile")
            ])
            .BuildDescriptors();

        descriptors.Select(descriptor => descriptor.Name).ShouldBe(
        [
            new ResourceName("first"),
            new ResourceName("second")
        ]);
    }

    [Fact]
    public void Catalog_exposes_descriptors_through_descriptor_provider_contract()
    {
        IResourceDescriptorProvider provider = new ResourceDescriptorCatalogBuilder()
            .Add(
                "primary",
                kind: "profile",
                displayName: "Primary Profile")
            .BuildCatalog();

        var descriptor = provider.GetResources().ShouldHaveSingleItem();

        descriptor.Name.ShouldBe(new ResourceName("primary"));
        descriptor.Kind.ShouldBe("profile");
        descriptor.DisplayName.ShouldBe("Primary Profile");
    }

    [Fact]
    public void Catalog_builder_uses_existing_catalog_validation()
    {
        var builder = new ResourceDescriptorCatalogBuilder()
            .Add(CreateDescriptor("primary", "profile"))
            .Add(CreateDescriptor("primary", "credential"));

        var exception = Should.Throw<InvalidOperationException>(() => builder.BuildCatalog());

        exception.Message.ShouldContain(nameof(ResourceDiagnosticCode.DuplicateResource));
    }

    [Fact]
    public void Catalog_builder_rejects_null_existing_descriptors()
    {
        var builder = new ResourceDescriptorCatalogBuilder();

        Should.Throw<ArgumentNullException>(() => builder.Add((ResourceDescriptor)null!));
    }

    [Fact]
    public void Catalog_builder_rejects_null_descriptor_ranges()
    {
        var builder = new ResourceDescriptorCatalogBuilder();

        Should.Throw<ArgumentNullException>(() => builder.AddRange(null!));
    }

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
    public void Diagnostic_metadata_is_copied_and_null_assignments_become_empty()
    {
        var metadata = new Dictionary<string, string>
        {
            ["path"] = "resources[0]"
        };

        var diagnostic = new ResourceDiagnostic
        {
            Code = ResourceDiagnosticCode.InvalidResource,
            Severity = ResourceDiagnosticSeverity.Error,
            Message = "Invalid resource.",
            Metadata = metadata
        };
        var emptyMetadataDiagnostic = new ResourceDiagnostic
        {
            Code = ResourceDiagnosticCode.MissingResource,
            Severity = ResourceDiagnosticSeverity.Error,
            Message = "Missing resource.",
            Metadata = null!
        };

        metadata["path"] = "changed";

        diagnostic.Metadata["path"].ShouldBe("resources[0]");
        emptyMetadataDiagnostic.Metadata.ShouldBeEmpty();
    }

    [Fact]
    public void Diagnostic_formatting_omits_metadata_values()
    {
        var diagnostic = new ResourceDiagnostic
        {
            Code = ResourceDiagnosticCode.InvalidResource,
            Severity = ResourceDiagnosticSeverity.Error,
            Message = "Invalid resource.",
            Metadata = new Dictionary<string, string>
            {
                ["accessToken"] = "secret-value"
            }
        };

        diagnostic.ToString().ShouldBe("Error InvalidResource: Invalid resource.");
        diagnostic.ToString().ShouldNotContain("secret-value");
    }

    [Fact]
    public void Default_resource_name_to_string_returns_empty()
    {
        default(ResourceName).ToString().ShouldBe(string.Empty);
    }

    [Fact]
    public void Resource_name_trims_surrounding_whitespace()
    {
        var name = new ResourceName("  primary  ");

        name.Value.ShouldBe("primary");
        name.ToString().ShouldBe("primary");
        name.ShouldBe(new ResourceName("primary"));
    }

    [Fact]
    public void Resource_descriptor_and_reference_text_fields_trim_surrounding_whitespace()
    {
        var descriptor = new ResourceDescriptor
        {
            Name = new ResourceName("primary"),
            Kind = " profile ",
            DisplayName = " Primary ",
            Summary = " Reusable profile. "
        };
        var reference = new ResourceReference
        {
            Name = new ResourceName("primary"),
            Kind = " profile "
        };

        descriptor.Kind.ShouldBe("profile");
        descriptor.DisplayName.ShouldBe("Primary");
        descriptor.Summary.ShouldBe("Reusable profile.");
        reference.Kind.ShouldBe("profile");
    }

    [Fact]
    public async Task Lookup_matches_trimmed_resource_names()
    {
        var descriptor = CreateDescriptor(" primary-profile ", "profile");
        var catalog = new ResourceDescriptorCatalog([descriptor]);

        var result = await catalog.LookupAsync(new ResourceReference
        {
            Name = new ResourceName("primary-profile"),
            Kind = "profile"
        });

        result.Found.ShouldBeTrue();
        result.Descriptor.ShouldBeSameAs(descriptor);
    }

    [Fact]
    public async Task Lookup_matches_trimmed_resource_kinds()
    {
        var descriptor = CreateDescriptor("primary-profile", " profile ");
        var catalog = new ResourceDescriptorCatalog([descriptor]);

        var result = await catalog.LookupAsync(new ResourceReference
        {
            Name = new ResourceName("primary-profile"),
            Kind = " profile "
        });

        result.Found.ShouldBeTrue();
        result.Diagnostic.ShouldBeNull();
    }

    [Fact]
    public void Descriptor_metadata_and_reference_attributes_trim_keys_and_values()
    {
        var descriptor = new ResourceDescriptor
        {
            Name = new ResourceName("primary"),
            Metadata = new Dictionary<string, string>
            {
                [" owner "] = " runtime "
            }
        };
        var reference = new ResourceReference
        {
            Name = new ResourceName("primary"),
            Attributes = new Dictionary<string, string>
            {
                [" scope "] = " workflow "
            }
        };

        descriptor.Metadata.ContainsKey("owner").ShouldBeTrue();
        descriptor.Metadata["owner"].ShouldBe("runtime");
        descriptor.Metadata.ContainsKey(" owner ").ShouldBeFalse();
        reference.Attributes.ContainsKey("scope").ShouldBeTrue();
        reference.Attributes["scope"].ShouldBe("workflow");
        reference.Attributes.ContainsKey(" scope ").ShouldBeFalse();
    }

    [Fact]
    public void Metadata_validation_reports_duplicate_keys_after_trimming()
    {
        var diagnostics = ResourceDiagnostics.ValidateDescriptors(
        [
            new ResourceDescriptor
            {
                Name = new ResourceName("primary"),
                Metadata = new Dictionary<string, string>
                {
                    ["owner"] = "runtime",
                    [" owner "] = "design"
                }
            }
        ]);

        diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == ResourceDiagnosticCode.InvalidResource
            && diagnostic.Metadata["path"] == "resources[0].metadata"
            && diagnostic.Message.Contains("after trimming", StringComparison.Ordinal));
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
    public void Duplicate_helper_reports_duplicate_names_after_trimming()
    {
        var diagnostics = ResourceDiagnostics.FindDuplicateResources(
        [
            CreateDescriptor(" primary ", "profile"),
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
    public void Descriptor_validation_reports_null_metadata()
    {
        var diagnostics = ResourceDiagnostics.ValidateDescriptors(
        [
            new ResourceDescriptor
            {
                Name = new ResourceName("primary"),
                Metadata = null!
            }
        ]);

        diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == ResourceDiagnosticCode.InvalidResource
            && diagnostic.Metadata["path"] == "resources[0].metadata"
            && diagnostic.Message.Contains("Map cannot be null.", StringComparison.Ordinal));
    }

    [Fact]
    public void Descriptor_validation_reports_null_descriptor_entries()
    {
        var diagnostics = ResourceDiagnostics.ValidateDescriptors([null!]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Code.ShouldBe(ResourceDiagnosticCode.InvalidResource);
        diagnostics[0].Severity.ShouldBe(ResourceDiagnosticSeverity.Error);
        diagnostics[0].Metadata["path"].ShouldBe("resources[0]");
        diagnostics[0].Message.ShouldContain("Resource descriptor is required.");
    }

    [Fact]
    public void Catalog_rejects_null_descriptors_with_structured_diagnostic()
    {
        var act = () => new ResourceDescriptorCatalog([null!]);

        var exception = act.ShouldThrow<InvalidOperationException>();
        exception.Message.ShouldContain(nameof(ResourceDiagnosticCode.InvalidResource));
        exception.Message.ShouldContain("Resource descriptor is required.");
    }

    [Fact]
    public void Reference_validation_reports_null_attributes()
    {
        var diagnostics = ResourceDiagnostics.ValidateReference(new ResourceReference
        {
            Name = new ResourceName("primary"),
            Attributes = null!
        });

        diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == ResourceDiagnosticCode.InvalidResource
            && diagnostic.Metadata["path"] == "reference.attributes"
            && diagnostic.Message.Contains("Map cannot be null.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_helper_reports_null_reference_entries()
    {
        var diagnostics = await ResourceDiagnostics.FindMissingResourcesAsync(
            new ResourceDescriptorCatalog([]),
            [null!]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Code.ShouldBe(ResourceDiagnosticCode.InvalidResource);
        diagnostics[0].Metadata["path"].ShouldBe("references[0]");
        diagnostics[0].Message.ShouldContain("Resource reference is required.");
    }

    [Fact]
    public void Unused_helper_ignores_null_entries()
    {
        var diagnostics = ResourceDiagnostics.FindUnusedResources(
            [CreateDescriptor("primary", "profile"), null!],
            [null!]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Code.ShouldBe(ResourceDiagnosticCode.UnusedResource);
        diagnostics[0].Name.ShouldBe(new ResourceName("primary"));
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
