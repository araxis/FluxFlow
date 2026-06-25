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
