using FluxFlow.Components.Resources.Contracts;
using FluxFlow.Composition.Addressing;
using Microsoft.Extensions.DependencyInjection;
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
                ResourceAddress("primary-profile"),
                ResourceOwnership.ResourceRevision,
                kind: " profile ",
                displayName: " Primary Profile ",
                summary: " Runtime profile. ",
                metadata: new Dictionary<string, string>
                {
                    [" owner "] = " runtime "
                })
            .BuildCatalog();

        var descriptor = catalog.GetResources().ShouldHaveSingleItem();
        descriptor.Name.ShouldBe(Resource("primary-profile"));
        descriptor.Ownership.ShouldBe(ResourceOwnership.ResourceRevision);
        descriptor.Kind.ShouldBe("profile");
        descriptor.DisplayName.ShouldBe("Primary Profile");
        descriptor.Summary.ShouldBe("Runtime profile.");
        descriptor.Metadata["owner"].ShouldBe("runtime");

        var result = await catalog.LookupAsync(new ResourceReference
        {
            Name = Resource("primary-profile"),
            Kind = "profile"
        });

        result.Found.ShouldBeTrue();
        result.Descriptor.ShouldBeSameAs(descriptor);
    }

    [Fact]
    public void Catalog_builder_accepts_typed_authoring_values()
    {
        var catalog = new ResourceDescriptorCatalogBuilder()
            .Add(
                Resource(" primary-profile "),
                ResourceOwnership.Host,
                kind: new ResourceKind(" profile "),
                displayName: new ResourceMetadataText(" Primary Profile "),
                summary: new ResourceMetadataText(" Runtime profile. "),
                metadata: new Dictionary<string, string>
                {
                    [" owner "] = " runtime "
                })
            .BuildCatalog();

        var descriptor = catalog.GetResources().ShouldHaveSingleItem();

        descriptor.Name.ShouldBe(Resource("primary-profile"));
        descriptor.Ownership.ShouldBe(ResourceOwnership.Host);
        descriptor.Kind.ShouldBe("profile");
        descriptor.DisplayName.ShouldBe("Primary Profile");
        descriptor.Summary.ShouldBe("Runtime profile.");
        descriptor.Metadata["owner"].ShouldBe("runtime");
    }

    [Fact]
    public void Catalog_builder_accepts_existing_descriptors_and_snapshots_build_results()
    {
        var builder = new ResourceDescriptorCatalogBuilder()
            .Add(CreateDescriptor("primary", "profile"));

        var descriptors = builder.BuildDescriptors();
        builder.Add(CreateDescriptor("secondary", "profile"));

        descriptors.Count.ShouldBe(1);
        descriptors[0].Name.ShouldBe(Resource("primary"));
        builder.BuildDescriptors().Select(descriptor => descriptor.Name).ShouldBe(
        [
            Resource("primary"),
            Resource("secondary")
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
            Resource("first"),
            Resource("second")
        ]);
    }

    [Fact]
    public void Lookup_result_factories_reject_invalid_arguments()
    {
        var reference = new ResourceReference { Name = Resource("primary") };
        var descriptor = CreateDescriptor("primary", "profile");

        Should.Throw<ArgumentNullException>(() =>
            ResourceLookupResult.FoundResult(null!, descriptor))
            .ParamName.ShouldBe("reference");
        Should.Throw<ArgumentNullException>(() =>
            ResourceLookupResult.FoundResult(reference, null!))
            .ParamName.ShouldBe("descriptor");
        Should.Throw<ArgumentNullException>(() =>
            ResourceLookupResult.Missing(null!))
            .ParamName.ShouldBe("reference");
        Should.Throw<ArgumentNullException>(() =>
            ResourceLookupResult.KindMismatch(null!, descriptor))
            .ParamName.ShouldBe("reference");
        Should.Throw<ArgumentNullException>(() =>
            ResourceLookupResult.KindMismatch(reference, null!))
            .ParamName.ShouldBe("descriptor");
    }

    [Fact]
    public void Catalog_exposes_descriptors_through_descriptor_provider_contract()
    {
        IResourceDescriptorProvider provider = new ResourceDescriptorCatalogBuilder()
            .Add(
                ResourceAddress("primary"),
                ResourceOwnership.Host,
                kind: "profile",
                displayName: "Primary Profile")
            .BuildCatalog();

        var descriptor = provider.GetResources().ShouldHaveSingleItem();

        descriptor.Name.ShouldBe(Resource("primary"));
        descriptor.Ownership.ShouldBe(ResourceOwnership.Host);
        descriptor.Kind.ShouldBe("profile");
        descriptor.DisplayName.ShouldBe("Primary Profile");
    }

    [Fact]
    public async Task Service_registration_registers_keyed_lookup_and_descriptor_provider_alias()
    {
        var catalog = new ResourceDescriptorCatalogBuilder()
            .Add(ResourceAddress("primary"), ResourceOwnership.ResourceRevision, kind: "profile")
            .BuildCatalog();
        var address = ResourceAddress("catalog");
        var services = new ServiceCollection()
            .AddExternalFluxFlowResourceLookup(address, catalog);

        using var provider = services.BuildServiceProvider();
        var lookup = provider.GetRequiredKeyedService<IResourceLookup>(address.Value);
        var descriptors = provider.GetRequiredKeyedService<IResourceDescriptorProvider>(address.Value);
        var result = await lookup.LookupAsync(new ResourceReference
        {
            Name = Resource("primary"),
            Kind = "profile"
        });

        lookup.ShouldBeSameAs(catalog);
        descriptors.GetResources().ShouldHaveSingleItem().ShouldBeSameAs(
            catalog.GetResources().ShouldHaveSingleItem());
        result.Found.ShouldBeTrue();
    }

    [Fact]
    public void Service_registration_registers_keyed_descriptor_provider()
    {
        var descriptor = CreateDescriptor("primary", "profile");
        var descriptorProvider = new StaticResourceDescriptorProvider([descriptor]);
        var address = ResourceAddress("descriptors");
        var services = new ServiceCollection()
            .AddExternalFluxFlowResourceDescriptorProvider(address, descriptorProvider);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IResourceDescriptorProvider>(address.Value);

        resolved.ShouldBeSameAs(descriptorProvider);
        resolved.GetResources().ShouldHaveSingleItem().ShouldBeSameAs(descriptor);
    }

    [Fact]
    public void Service_registration_uses_canonical_nested_address_keys()
    {
        var catalog = new ResourceDescriptorCatalogBuilder()
            .Add(ResourceAddress("primary"), ResourceOwnership.Host, kind: "profile")
            .BuildCatalog();
        var descriptorProvider = new StaticResourceDescriptorProvider(
        [
            CreateDescriptor("secondary", "profile")
        ]);

        var lookupAddress = ApplicationAddress.Resource("Catalogs", "Primary");
        var descriptorAddress = ApplicationAddress.Resource("Catalogs", "Declared");
        var services = new ServiceCollection()
            .AddExternalFluxFlowResourceLookup(lookupAddress, catalog)
            .AddExternalFluxFlowResourceDescriptorProvider(descriptorAddress, descriptorProvider);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IResourceLookup>(lookupAddress.Value).ShouldBeSameAs(catalog);
        provider.GetRequiredKeyedService<IResourceDescriptorProvider>(lookupAddress.Value)
            .GetResources().ShouldHaveSingleItem();
        provider.GetRequiredKeyedService<IResourceDescriptorProvider>(descriptorAddress.Value)
            .ShouldBeSameAs(descriptorProvider);
    }

    [Fact]
    public void Service_registration_passes_provider_to_factories()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ResourceRegistrationDependency("primary"));
        var lookupAddress = ResourceAddress("lookup");
        var providerAddress = ResourceAddress("provider");
        services
            .AddFluxFlowResourceLookup(
                lookupAddress,
                provider => new ResourceDescriptorCatalogBuilder()
                    .Add(
                        ResourceAddress(provider.GetRequiredService<ResourceRegistrationDependency>().Name),
                        ResourceOwnership.Host,
                        kind: "profile")
                    .BuildCatalog())
            .AddFluxFlowResourceDescriptorProvider(
                providerAddress,
                provider => new StaticResourceDescriptorProvider(
                [
                    CreateDescriptor(
                        provider.GetRequiredService<ResourceRegistrationDependency>().Name,
                        "profile")
                ]));

        using var provider = services.BuildServiceProvider();
        var lookup = provider.GetRequiredKeyedService<IResourceLookup>(lookupAddress.Value);
        var descriptors = provider.GetRequiredKeyedService<IResourceDescriptorProvider>(providerAddress.Value)
            .GetResources();

        lookup.GetResources().ShouldHaveSingleItem().Name.ShouldBe(Resource("primary"));
        descriptors.ShouldHaveSingleItem().Name.ShouldBe(Resource("primary"));
    }

    [Fact]
    public void Service_registration_rejects_invalid_arguments()
    {
        var services = new ServiceCollection();
        var lookup = new ResourceDescriptorCatalog([]);
        var descriptorProvider = new StaticResourceDescriptorProvider([]);
        var address = ResourceAddress("catalog");
        var componentAddress = ApplicationAddress.WorkflowComponent("Workflow", "Component");

        Should.Throw<ArgumentNullException>(() =>
            ResourceServiceCollectionExtensions.AddFluxFlowResourceLookup(
                null!, address, _ => lookup))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            ResourceServiceCollectionExtensions.AddExternalFluxFlowResourceLookup(
                null!, address, lookup))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            services.AddExternalFluxFlowResourceLookup(address, null!))
            .ParamName.ShouldBe("lookup");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowResourceLookup(address, null!))
            .ParamName.ShouldBe("lookupFactory");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowResourceLookup(componentAddress, _ => lookup))
            .ParamName.ShouldBe("address");
        Should.Throw<ArgumentException>(() =>
            services.AddExternalFluxFlowResourceLookup(componentAddress, lookup))
            .ParamName.ShouldBe("address");

        Should.Throw<ArgumentNullException>(() =>
            ResourceServiceCollectionExtensions.AddFluxFlowResourceDescriptorProvider(
                null!,
                address,
                _ => descriptorProvider))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            ResourceServiceCollectionExtensions.AddExternalFluxFlowResourceDescriptorProvider(
                null!,
                address,
                descriptorProvider))
            .ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            services.AddExternalFluxFlowResourceDescriptorProvider(address, null!))
            .ParamName.ShouldBe("descriptorProvider");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowResourceDescriptorProvider(address, null!))
            .ParamName.ShouldBe("descriptorProviderFactory");
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowResourceDescriptorProvider(componentAddress, _ => descriptorProvider))
            .ParamName.ShouldBe("address");
        Should.Throw<ArgumentException>(() =>
            services.AddExternalFluxFlowResourceDescriptorProvider(componentAddress, descriptorProvider))
            .ParamName.ShouldBe("address");
    }

    [Fact]
    public void Service_registration_rejects_null_factory_results()
    {
        var lookupAddress = ResourceAddress("lookup");
        var providerAddress = ResourceAddress("provider");
        var services = new ServiceCollection()
            .AddFluxFlowResourceLookup(lookupAddress, _ => null!)
            .AddFluxFlowResourceDescriptorProvider(providerAddress, _ => null!);

        using var provider = services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<IResourceLookup>(lookupAddress.Value))
            .Message.ShouldContain("Resource lookup factory returned null.");
        Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredKeyedService<IResourceDescriptorProvider>(providerAddress.Value))
            .Message.ShouldContain("Resource descriptor provider factory returned null.");
    }

    [Fact]
    public void Provider_created_lookup_is_disposed_once_and_metadata_alias_is_nonowning()
    {
        var address = ResourceAddress("owned-lookup");
        DisposableResourceLookup? lookup = null;
        var services = new ServiceCollection()
            .AddFluxFlowResourceLookup(address, _ => lookup = new DisposableResourceLookup());

        using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredKeyedService<IResourceLookup>(address.Value)
                .ShouldBeSameAs(lookup);
            provider.GetRequiredKeyedService<IResourceDescriptorProvider>(address.Value)
                .GetResources().ShouldBeEmpty();
        }

        lookup.ShouldNotBeNull().DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void External_lookup_registration_does_not_transfer_disposal_ownership()
    {
        var address = ResourceAddress("external-lookup");
        var lookup = new DisposableResourceLookup();
        var services = new ServiceCollection()
            .AddExternalFluxFlowResourceLookup(address, lookup);

        using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredKeyedService<IResourceLookup>(address.Value)
                .ShouldBeSameAs(lookup);
            provider.GetRequiredKeyedService<IResourceDescriptorProvider>(address.Value)
                .GetResources().ShouldBeEmpty();
        }

        lookup.DisposeCount.ShouldBe(0);
        lookup.Dispose();
        lookup.DisposeCount.ShouldBe(1);
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
            Name = Resource("primary-profile"),
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
            Name = Resource("secondary-profile"),
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
            Name = Resource("primary"),
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
    public void Resource_name_uses_canonical_nested_application_address()
    {
        var address = ApplicationAddress.Resource("Messaging", "Primary");
        var name = new ResourceName(address);

        name.Value.ShouldBe("Resources.Messaging.Primary");
        name.ToString().ShouldBe("Resources.Messaging.Primary");
        name.Address.ShouldBe(address);
        new ResourceName(address.Value).ShouldBe(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("primary")]
    [InlineData("Workflow.Component")]
    [InlineData(" Resources.Messaging.Primary ")]
    public void Resource_name_rejects_noncanonical_or_nonresource_addresses(string value)
    {
        Should.Throw<ArgumentException>(() => new ResourceName(value))
            .ParamName.ShouldBe("value");
    }

    [Fact]
    public void Resource_kind_trims_surrounding_whitespace()
    {
        var kind = new ResourceKind("  profile  ");

        kind.Value.ShouldBe("profile");
        kind.ToString().ShouldBe("profile");
        kind.ShouldBe(new ResourceKind("profile"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Resource_kind_rejects_empty_values(string value)
    {
        Should.Throw<ArgumentException>(() => new ResourceKind(value))
            .ParamName.ShouldBe("value");
    }

    [Fact]
    public void Default_resource_kind_to_string_returns_empty()
    {
        default(ResourceKind).ToString().ShouldBe(string.Empty);
    }

    [Fact]
    public void Resource_metadata_text_trims_surrounding_whitespace()
    {
        var text = new ResourceMetadataText("  Primary Profile  ");

        text.Value.ShouldBe("Primary Profile");
        text.ToString().ShouldBe("Primary Profile");
        text.ShouldBe(new ResourceMetadataText("Primary Profile"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Resource_metadata_text_rejects_empty_values(string value)
    {
        Should.Throw<ArgumentException>(() => new ResourceMetadataText(value))
            .ParamName.ShouldBe("value");
    }

    [Fact]
    public void Default_resource_metadata_text_to_string_returns_empty()
    {
        default(ResourceMetadataText).ToString().ShouldBe(string.Empty);
    }

    [Fact]
    public void Catalog_builder_typed_authoring_rejects_default_resource_name()
    {
        Should.Throw<ArgumentException>(() =>
                new ResourceDescriptorCatalogBuilder().Add(
                    default(ResourceName),
                    ResourceOwnership.Host))
            .ParamName.ShouldBe("name");
    }

    [Fact]
    public void Resource_descriptor_and_reference_text_fields_trim_surrounding_whitespace()
    {
        var descriptor = new ResourceDescriptor
        {
            Name = Resource("primary"),
            Ownership = ResourceOwnership.Host,
            Kind = " profile ",
            DisplayName = " Primary ",
            Summary = " Reusable profile. "
        };
        var reference = new ResourceReference
        {
            Name = Resource("primary"),
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
            Name = Resource("primary-profile"),
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
            Name = Resource("primary-profile"),
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
            Name = Resource("primary"),
            Ownership = ResourceOwnership.Host,
            Metadata = new Dictionary<string, string>
            {
                [" owner "] = " runtime "
            }
        };
        var reference = new ResourceReference
        {
            Name = Resource("primary"),
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
                Name = Resource("primary"),
                Ownership = ResourceOwnership.Host,
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
        diagnostics[0].Name.ShouldBe(Resource("primary"));
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
        diagnostics[0].Name.ShouldBe(Resource("primary"));
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
                new ResourceReference { Name = Resource("primary"), Kind = "profile" },
                new ResourceReference { Name = Resource("primary"), Kind = "credential" },
                new ResourceReference { Name = Resource("secondary"), Kind = "profile" }
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
                new ResourceReference { Name = Resource("primary"), Kind = "profile" }
            ]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Code.ShouldBe(ResourceDiagnosticCode.UnusedResource);
        diagnostics[0].Name.ShouldBe(Resource("secondary"));
    }

    [Fact]
    public void Descriptor_validation_reports_default_name_and_empty_metadata()
    {
        var diagnostics = ResourceDiagnostics.ValidateDescriptors(
        [
            new ResourceDescriptor
            {
                Name = default,
                Ownership = (ResourceOwnership)0,
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
                Name = Resource("primary"),
                Ownership = ResourceOwnership.Host,
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
            Name = Resource("primary"),
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
        diagnostics[0].Name.ShouldBe(Resource("primary"));
    }

    [Fact]
    public void Reference_attributes_are_preserved()
    {
        var reference = new ResourceReference
        {
            Name = Resource("primary"),
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
        Name = Resource(name),
        Ownership = ResourceOwnership.ResourceRevision,
        Kind = kind,
        DisplayName = "Primary",
        Summary = "Reusable profile.",
        Metadata = new Dictionary<string, string>
        {
            ["owner"] = "runtime"
        }
    };

    private static ApplicationAddress ResourceAddress(string name)
        => ApplicationAddress.Resource("Tests", name.Trim());

    private static ResourceName Resource(string name) => new(ResourceAddress(name));

    private sealed record ResourceRegistrationDependency(string Name);

    private sealed class StaticResourceDescriptorProvider(
        IReadOnlyCollection<ResourceDescriptor> descriptors) : IResourceDescriptorProvider
    {
        public IReadOnlyCollection<ResourceDescriptor> GetResources() => descriptors;
    }

    private sealed class DisposableResourceLookup : IResourceLookup, IDisposable
    {
        public int DisposeCount { get; private set; }

        public IReadOnlyCollection<ResourceDescriptor> GetResources() => [];

        public ValueTask<ResourceLookupResult> LookupAsync(
            ResourceReference reference,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ResourceLookupResult.Missing(reference));

        public void Dispose() => DisposeCount++;
    }
}
