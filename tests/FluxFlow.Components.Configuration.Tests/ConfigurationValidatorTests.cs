using FluxFlow.Components.Configuration.Contracts;
using FluxFlow.Components.Resources;
using FluxFlow.Components.Resources.Contracts;
using FluxFlow.Components.Secrets;
using FluxFlow.Components.Secrets.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Configuration.Tests;

public sealed class ConfigurationValidatorTests
{
    [Fact]
    public void Configuration_diagnostic_normalizes_text_and_copies_metadata()
    {
        var metadata = new Dictionary<string, string>
        {
            ["referencePath"] = "resource.name"
        };

        var diagnostic = new ConfigurationDiagnostic
        {
            Source = ConfigurationDiagnosticSource.Resource,
            Code = " InvalidResource ",
            Severity = ConfigurationDiagnosticSeverity.Error,
            Message = " Resource failed. ",
            Path = " connections.primary ",
            Name = " primary ",
            Kind = " connection ",
            Metadata = metadata
        };

        metadata["referencePath"] = "changed";

        diagnostic.Code.ShouldBe("InvalidResource");
        diagnostic.Message.ShouldBe("Resource failed.");
        diagnostic.Path.ShouldBe("connections.primary");
        diagnostic.Name.ShouldBe("primary");
        diagnostic.Kind.ShouldBe("connection");
        diagnostic.Metadata["referencePath"].ShouldBe("resource.name");
        diagnostic.ToString().ShouldBe("Error Resource.InvalidResource at connections.primary: Resource failed.");
    }

    [Fact]
    public void Configuration_diagnostic_treats_blank_optional_text_as_absent()
    {
        var diagnostic = new ConfigurationDiagnostic
        {
            Source = ConfigurationDiagnosticSource.Configuration,
            Code = "Invalid",
            Severity = ConfigurationDiagnosticSeverity.Warning,
            Message = "Message",
            Path = " ",
            Name = " ",
            Kind = " ",
            Metadata = null!
        };

        diagnostic.Path.ShouldBeNull();
        diagnostic.Name.ShouldBeNull();
        diagnostic.Kind.ShouldBeNull();
        diagnostic.Metadata.ShouldBeEmpty();
        diagnostic.ToString().ShouldBe("Warning Configuration.Invalid: Message");
    }

    [Fact]
    public void Validation_report_copies_diagnostics_and_handles_null_assignment()
    {
        var diagnostic = new ConfigurationDiagnostic
        {
            Source = ConfigurationDiagnosticSource.Configuration,
            Code = "Invalid",
            Severity = ConfigurationDiagnosticSeverity.Error,
            Message = "Invalid configuration."
        };
        var diagnostics = new List<ConfigurationDiagnostic> { diagnostic };

        var report = ConfigurationValidationReport.FromDiagnostics(diagnostics);
        diagnostics.Clear();
        var empty = new ConfigurationValidationReport { Diagnostics = null! };

        report.Diagnostics.Count.ShouldBe(1);
        report.HasErrors.ShouldBeTrue();
        report.ErrorCount.ShouldBe(1);
        empty.Diagnostics.ShouldBeEmpty();
        empty.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_returns_empty_report_for_valid_references()
    {
        var resourceLookup = new ResourceDescriptorCatalog(
        [
            new ResourceDescriptor
            {
                Name = new ResourceName("primary"),
                Kind = "connection"
            }
        ]);
        var secretResolver = new InMemorySecretResolver(
        [
            CreateSecret("primary-credential", "runtime-value")
        ]);

        var report = await ConfigurationValidator.ValidateAsync(
            resourceLookup,
            secretResolver,
            new ConfigurationValidationRequest
            {
                Resources =
                [
                    new ConfigurationResourceReference
                    {
                        Path = "connections.primary",
                        Reference = new ResourceReference
                        {
                            Name = new ResourceName("primary"),
                            Kind = "connection"
                        }
                    }
                ],
                Secrets =
                [
                    new SecretOptionReference
                    {
                        OptionPath = "connections.primary.credential",
                        Reference = new SecretReference
                        {
                            Name = new SecretName("primary-credential")
                        }
                    }
                ]
            });

        report.Diagnostics.ShouldBeEmpty();
        report.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateAsync_combines_resource_and_secret_diagnostics()
    {
        var resourceLookup = new ResourceDescriptorCatalog([]);
        var secretResolver = new InMemorySecretResolver([]);

        var report = await ConfigurationValidator.ValidateAsync(
            resourceLookup,
            secretResolver,
            new ConfigurationValidationRequest
            {
                Resources =
                [
                    new ConfigurationResourceReference
                    {
                        Path = "connections.primary",
                        Reference = new ResourceReference
                        {
                            Name = new ResourceName("missing"),
                            Kind = "connection"
                        }
                    }
                ],
                Secrets =
                [
                    new SecretOptionReference
                    {
                        OptionPath = "connections.primary.credential",
                        Reference = new SecretReference
                        {
                            Name = new SecretName("missing-credential")
                        }
                    }
                ]
            });

        report.HasErrors.ShouldBeTrue();
        report.ErrorCount.ShouldBe(2);
        report.Diagnostics.Select(diagnostic => diagnostic.Source).ShouldBe(
        [
            ConfigurationDiagnosticSource.Resource,
            ConfigurationDiagnosticSource.Secret
        ]);
        report.Diagnostics.Select(diagnostic => diagnostic.Path).ShouldBe(
        [
            "connections.primary",
            "connections.primary.credential"
        ]);
    }

    [Fact]
    public async Task Optional_missing_references_do_not_report_errors()
    {
        var resourceLookup = new ResourceDescriptorCatalog([]);
        var secretResolver = new InMemorySecretResolver([]);

        var report = await ConfigurationValidator.ValidateAsync(
            resourceLookup,
            secretResolver,
            new ConfigurationValidationRequest
            {
                Resources =
                [
                    new ConfigurationResourceReference
                    {
                        Path = "connections.optional",
                        Required = false
                    }
                ],
                Secrets =
                [
                    new SecretOptionReference
                    {
                        OptionPath = "connections.optional.credential",
                        Required = false
                    }
                ]
            });

        report.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_reports_null_request_collections_as_diagnostics()
    {
        var report = await ConfigurationValidator.ValidateAsync(
            new ResourceDescriptorCatalog([]),
            new InMemorySecretResolver([]),
            new ConfigurationValidationRequest
            {
                Resources = null!,
                Secrets = null!
            });

        report.HasErrors.ShouldBeTrue();
        report.ErrorCount.ShouldBe(2);
        report.Diagnostics.ShouldAllBe(diagnostic =>
            diagnostic.Source == ConfigurationDiagnosticSource.Configuration);
        report.Diagnostics.ShouldAllBe(diagnostic =>
            diagnostic.Code == "InvalidConfigurationValidationRequest");
        report.Diagnostics.Select(diagnostic => diagnostic.Path).ShouldBe(
        [
            "resources",
            "secrets"
        ]);
        report.Diagnostics.Select(diagnostic => diagnostic.Metadata["referencePath"]).ShouldBe(
        [
            "request.resources",
            "request.secrets"
        ]);
    }

    [Fact]
    public async Task Invalid_resource_reference_includes_option_and_reference_paths()
    {
        var diagnostics = await ConfigurationValidator.ValidateResourcesAsync(
            new ResourceDescriptorCatalog([]),
            [
                new ConfigurationResourceReference
                {
                    Path = "connections.primary",
                    Reference = new ResourceReference { Name = default }
                }
            ]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Source.ShouldBe(ConfigurationDiagnosticSource.Resource);
        diagnostics[0].Code.ShouldBe(nameof(ResourceDiagnosticCode.InvalidResource));
        diagnostics[0].Path.ShouldBe("connections.primary");
        diagnostics[0].Metadata["path"].ShouldBe("connections.primary");
        diagnostics[0].Metadata["referencePath"].ShouldBe("reference.name");
    }

    [Fact]
    public async Task ValidateResourcesAsync_reports_null_entries()
    {
        var diagnostics = await ConfigurationValidator.ValidateResourcesAsync(
            new ResourceDescriptorCatalog([]),
            [null!]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Source.ShouldBe(ConfigurationDiagnosticSource.Configuration);
        diagnostics[0].Code.ShouldBe("InvalidConfigurationValidationRequest");
        diagnostics[0].Path.ShouldBe("resources[0]");
        diagnostics[0].Metadata["referencePath"].ShouldBe("resources[0]");
    }

    [Fact]
    public async Task Missing_required_resource_reference_is_reported()
    {
        var diagnostics = await ConfigurationValidator.ValidateResourcesAsync(
            new ResourceDescriptorCatalog([]),
            [
                new ConfigurationResourceReference
                {
                    Path = "connections.primary"
                }
            ]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Source.ShouldBe(ConfigurationDiagnosticSource.Resource);
        diagnostics[0].Code.ShouldBe("MissingResourceReference");
        diagnostics[0].Path.ShouldBe("connections.primary");
    }

    [Fact]
    public async Task ValidateResourcesAsync_reports_invalid_resource_option_metadata()
    {
        var diagnostics = await ConfigurationValidator.ValidateResourcesAsync(
            new ResourceDescriptorCatalog([]),
            [
                new ConfigurationResourceReference
                {
                    Path = "connections.primary",
                    Required = false,
                    Metadata = null!
                },
                new ConfigurationResourceReference
                {
                    Path = "connections.secondary",
                    Required = false,
                    Metadata = new Dictionary<string, string>
                    {
                        [""] = "value",
                        ["empty"] = ""
                    }
                }
            ]);

        diagnostics.Count.ShouldBe(3);
        diagnostics.ShouldAllBe(diagnostic => diagnostic.Source == ConfigurationDiagnosticSource.Configuration);
        diagnostics.ShouldAllBe(diagnostic => diagnostic.Code == "InvalidResourceReference");
        diagnostics.Select(diagnostic => diagnostic.Path).ShouldBe(
        [
            "connections.primary",
            "connections.secondary",
            "connections.secondary"
        ]);
        diagnostics.Select(diagnostic => diagnostic.Metadata["referencePath"]).ShouldBe(
        [
            "resource.metadata",
            "resource.metadata",
            "resource.metadata.empty"
        ]);
    }

    [Fact]
    public async Task ValidateSecretsAsync_uses_option_paths()
    {
        var diagnostics = await ConfigurationValidator.ValidateSecretsAsync(
            new InMemorySecretResolver([]),
            [
                new SecretOptionReference
                {
                    OptionPath = "connections.primary.credential",
                    Reference = new SecretReference { Name = new SecretName("missing") }
                }
            ]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Source.ShouldBe(ConfigurationDiagnosticSource.Secret);
        diagnostics[0].Code.ShouldBe(nameof(SecretDiagnosticCode.MissingSecret));
        diagnostics[0].Path.ShouldBe("connections.primary.credential");
    }

    [Fact]
    public async Task ValidateResourcesAsync_trims_resource_option_paths()
    {
        var diagnostics = await ConfigurationValidator.ValidateResourcesAsync(
            new ResourceDescriptorCatalog([]),
            [
                new ConfigurationResourceReference
                {
                    Path = " connections.primary ",
                    Reference = new ResourceReference { Name = new ResourceName("missing") }
                }
            ]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Path.ShouldBe("connections.primary");
        diagnostics[0].Metadata["path"].ShouldBe("connections.primary");
    }

    [Fact]
    public void Resource_option_metadata_trims_keys_and_values()
    {
        var option = new ConfigurationResourceReference
        {
            Path = "connections.primary",
            Metadata = new Dictionary<string, string>
            {
                [" owner "] = " runtime "
            }
        };

        option.Metadata.ContainsKey("owner").ShouldBeTrue();
        option.Metadata["owner"].ShouldBe("runtime");
        option.Metadata.ContainsKey(" owner ").ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateResourcesAsync_reports_duplicate_metadata_keys_after_trimming()
    {
        var diagnostics = await ConfigurationValidator.ValidateResourcesAsync(
            new ResourceDescriptorCatalog([]),
            [
                new ConfigurationResourceReference
                {
                    Path = "connections.primary",
                    Required = false,
                    Metadata = new Dictionary<string, string>
                    {
                        ["owner"] = "runtime",
                        [" owner "] = "design"
                    }
                }
            ]);

        diagnostics.ShouldContain(diagnostic =>
            diagnostic.Source == ConfigurationDiagnosticSource.Configuration
            && diagnostic.Code == "InvalidResourceReference"
            && diagnostic.Metadata["referencePath"] == "resource.metadata"
            && diagnostic.Message.Contains("after trimming", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateSecretsAsync_reports_null_entries()
    {
        var diagnostics = await ConfigurationValidator.ValidateSecretsAsync(
            new InMemorySecretResolver([]),
            [null!]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Source.ShouldBe(ConfigurationDiagnosticSource.Configuration);
        diagnostics[0].Code.ShouldBe("InvalidConfigurationValidationRequest");
        diagnostics[0].Path.ShouldBe("secrets[0]");
        diagnostics[0].Metadata["referencePath"].ShouldBe("secrets[0]");
    }

    [Fact]
    public async Task ValidateSecretsAsync_preserves_all_option_validation_diagnostics()
    {
        var diagnostics = await ConfigurationValidator.ValidateSecretsAsync(
            new InMemorySecretResolver([]),
            [
                new SecretOptionReference
                {
                    OptionPath = "connections.primary.credential",
                    Reference = new SecretReference
                    {
                        Name = default,
                        Version = " "
                    }
                }
            ]);

        diagnostics.Count.ShouldBe(2);
        diagnostics.ShouldAllBe(diagnostic => diagnostic.Source == ConfigurationDiagnosticSource.Secret);
        diagnostics.ShouldAllBe(diagnostic => diagnostic.Code == nameof(SecretDiagnosticCode.InvalidSecret));
        diagnostics.ShouldAllBe(diagnostic => diagnostic.Path == "connections.primary.credential");
        diagnostics.Select(diagnostic => diagnostic.Metadata["referencePath"]).ShouldBe(
        [
            "reference.name",
            "reference.version"
        ]);
    }

    [Fact]
    public async Task ValidateAsync_observes_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await ConfigurationValidator.ValidateAsync(
            new ResourceDescriptorCatalog([]),
            new InMemorySecretResolver([]),
            new ConfigurationValidationRequest
            {
                Resources =
                [
                    new ConfigurationResourceReference
                    {
                        Path = "connections.primary",
                        Required = false
                    }
                ]
            },
            cts.Token);

        await act.ShouldThrowAsync<OperationCanceledException>();
    }

    private static SecretRecord CreateSecret(string name, string value)
        => new()
        {
            Descriptor = new SecretDescriptor
            {
                Name = new SecretName(name)
            },
            Value = new SecretValue(value)
        };
}
