using FluxFlow.Components.Secrets.Contracts;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace FluxFlow.Components.Secrets.Tests;

public sealed class SecretResolverTests
{
    [Fact]
    public async Task Resolver_builder_creates_normalized_record_and_resolver()
    {
        var resolver = new InMemorySecretResolverBuilder()
            .Add(
                " primary-token ",
                "runtime-value",
                version: " v1 ",
                kind: " profile ",
                displayName: " Primary Token ",
                summary: " Runtime credential. ",
                metadata: new Dictionary<string, string>
                {
                    [" owner "] = " runtime "
                })
            .BuildResolver();

        var descriptor = resolver.GetDescriptors().ShouldHaveSingleItem();
        descriptor.Name.ShouldBe(new SecretName("primary-token"));
        descriptor.Version.ShouldBe("v1");
        descriptor.Kind.ShouldBe("profile");
        descriptor.DisplayName.ShouldBe("Primary Token");
        descriptor.Summary.ShouldBe("Runtime credential.");
        descriptor.Metadata["owner"].ShouldBe("runtime");

        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = new SecretName("primary-token"),
            Version = "v1",
            Kind = "profile"
        });

        result.Resolved.ShouldBeTrue();
        result.Value.ShouldNotBeNull().Reveal().ShouldBe("runtime-value");
        result.Value.ToString().ShouldBe(SecretRedactor.RedactedText);
    }

    [Fact]
    public void Resolver_builder_accepts_existing_records_and_snapshots_build_results()
    {
        var builder = new InMemorySecretResolverBuilder()
            .Add(CreateRecord("primary", "one"));

        var records = builder.BuildRecords();
        builder.Add(CreateRecord("secondary", "two"));

        records.Count.ShouldBe(1);
        records[0].Descriptor.Name.ShouldBe(new SecretName("primary"));
        builder.BuildRecords().Select(record => record.Descriptor.Name).ShouldBe(
        [
            new SecretName("primary"),
            new SecretName("secondary")
        ]);
    }

    [Fact]
    public void Resolver_builder_add_range_preserves_order()
    {
        var records = new InMemorySecretResolverBuilder()
            .AddRange(
            [
                CreateRecord("first", "one"),
                CreateRecord("second", "two")
            ])
            .BuildRecords();

        records.Select(record => record.Descriptor.Name).ShouldBe(
        [
            new SecretName("first"),
            new SecretName("second")
        ]);
    }

    [Fact]
    public void Resolver_builder_accepts_existing_secret_values()
    {
        var value = new SecretValue("runtime-value");
        var record = new InMemorySecretResolverBuilder()
            .Add("primary", value)
            .BuildRecords()
            .ShouldHaveSingleItem();

        record.Value.ShouldBeSameAs(value);
        record.Value.ToString().ShouldBe(SecretRedactor.RedactedText);
    }

    [Fact]
    public void Resolver_exposes_descriptors_through_optional_provider_contract()
    {
        ISecretDescriptorProvider provider = new InMemorySecretResolverBuilder()
            .Add(
                "primary",
                "runtime-value",
                version: "v1",
                kind: "profile",
                displayName: "Primary")
            .BuildResolver();

        var descriptor = provider.GetDescriptors().ShouldHaveSingleItem();

        descriptor.Name.ShouldBe(new SecretName("primary"));
        descriptor.Version.ShouldBe("v1");
        descriptor.Kind.ShouldBe("profile");
        descriptor.DisplayName.ShouldBe("Primary");
        descriptor.ToString().ShouldNotContain("runtime-value");
    }

    [Fact]
    public void Resolver_builder_uses_existing_resolver_validation()
    {
        var builder = new InMemorySecretResolverBuilder()
            .Add("primary", "one", version: "v1")
            .Add("primary", "two", version: "v1");

        var exception = Should.Throw<InvalidOperationException>(() => builder.BuildResolver());

        exception.Message.ShouldContain(nameof(SecretDiagnosticCode.DuplicateSecret));
    }

    [Fact]
    public void Resolver_builder_rejects_null_existing_records()
    {
        var builder = new InMemorySecretResolverBuilder();

        Should.Throw<ArgumentNullException>(() => builder.Add((SecretRecord)null!));
    }

    [Fact]
    public void Resolver_builder_rejects_null_record_ranges()
    {
        var builder = new InMemorySecretResolverBuilder();

        Should.Throw<ArgumentNullException>(() => builder.AddRange(null!));
    }

    [Fact]
    public void Resolver_builder_rejects_null_secret_values()
    {
        var builder = new InMemorySecretResolverBuilder();

        Should.Throw<ArgumentNullException>(() => builder.Add("primary", (SecretValue)null!));
    }

    [Fact]
    public void Resolve_result_factories_reject_invalid_arguments()
    {
        var reference = new SecretReference { Name = new SecretName("primary") };
        var descriptor = new SecretDescriptor { Name = new SecretName("primary") };
        var value = new SecretValue("runtime-value");

        Should.Throw<ArgumentNullException>(() =>
            SecretResolveResult.ResolvedResult(null!, descriptor, value))
            .ParamName.ShouldBe("reference");
        Should.Throw<ArgumentNullException>(() =>
            SecretResolveResult.ResolvedResult(reference, null!, value))
            .ParamName.ShouldBe("descriptor");
        Should.Throw<ArgumentNullException>(() =>
            SecretResolveResult.ResolvedResult(reference, descriptor, null!))
            .ParamName.ShouldBe("value");
        Should.Throw<ArgumentNullException>(() =>
            SecretResolveResult.Missing(null!))
            .ParamName.ShouldBe("reference");
        Should.Throw<ArgumentNullException>(() =>
            SecretResolveResult.Ambiguous(null!, [descriptor]))
            .ParamName.ShouldBe("reference");
        Should.Throw<ArgumentNullException>(() =>
            SecretResolveResult.Ambiguous(reference, null!))
            .ParamName.ShouldBe("matches");
        Should.Throw<ArgumentNullException>(() =>
            SecretResolveResult.KindMismatch(null!, descriptor))
            .ParamName.ShouldBe("reference");
        Should.Throw<ArgumentNullException>(() =>
            SecretResolveResult.KindMismatch(reference, null!))
            .ParamName.ShouldBe("descriptor");
        Should.Throw<ArgumentNullException>(() =>
            SecretResolveResult.AccessDenied(null!, "Denied."))
            .ParamName.ShouldBe("reference");
        Should.Throw<ArgumentException>(() =>
            SecretResolveResult.AccessDenied(reference, " "))
            .ParamName.ShouldBe("message");
        Should.Throw<ArgumentNullException>(() =>
            SecretResolveResult.Failed(null!, "Failed."))
            .ParamName.ShouldBe("reference");
        Should.Throw<ArgumentException>(() =>
            SecretResolveResult.Failed(reference, " "))
            .ParamName.ShouldBe("message");
    }

    [Fact]
    public async Task Resolver_returns_value_for_matching_reference()
    {
        var resolver = new InMemorySecretResolver(
        [
            CreateRecord("primary-token", "runtime-value", kind: "profile")
        ]);

        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = new SecretName("primary-token"),
            Kind = "profile"
        });

        result.Resolved.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Reveal().ShouldBe("runtime-value");
        result.Value.ToString().ShouldBe(SecretRedactor.RedactedText);
    }

    [Fact]
    public void Secret_name_trims_surrounding_whitespace()
    {
        var name = new SecretName("  primary-token  ");

        name.Value.ShouldBe("primary-token");
        name.ToString().ShouldBe("primary-token");
        name.ShouldBe(new SecretName("primary-token"));
    }

    [Fact]
    public void Secret_descriptor_and_reference_text_fields_trim_surrounding_whitespace()
    {
        var descriptor = new SecretDescriptor
        {
            Name = new SecretName("primary"),
            Version = " v1 ",
            Kind = " profile ",
            DisplayName = " Primary ",
            Summary = " Runtime credential. "
        };
        var reference = new SecretReference
        {
            Name = new SecretName("primary"),
            Version = " v1 ",
            Kind = " profile "
        };

        descriptor.Version.ShouldBe("v1");
        descriptor.Kind.ShouldBe("profile");
        descriptor.DisplayName.ShouldBe("Primary");
        descriptor.Summary.ShouldBe("Runtime credential.");
        reference.Version.ShouldBe("v1");
        reference.Kind.ShouldBe("profile");
    }

    [Fact]
    public async Task Resolver_matches_trimmed_secret_names()
    {
        var resolver = new InMemorySecretResolver(
        [
            CreateRecord(" primary-token ", "runtime-value", kind: "profile")
        ]);

        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = new SecretName("primary-token"),
            Kind = "profile"
        });

        result.Resolved.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Reveal().ShouldBe("runtime-value");
    }

    [Fact]
    public async Task Resolver_matches_trimmed_version_and_kind()
    {
        var resolver = new InMemorySecretResolver(
        [
            CreateRecord("primary-token", "runtime-value", version: " v1 ", kind: " profile ")
        ]);

        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = new SecretName("primary-token"),
            Version = " v1 ",
            Kind = " profile "
        });

        result.Resolved.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Reveal().ShouldBe("runtime-value");
    }

    [Fact]
    public void Secret_metadata_and_reference_attributes_trim_keys_and_values()
    {
        var descriptor = new SecretDescriptor
        {
            Name = new SecretName("primary"),
            Metadata = new Dictionary<string, string>
            {
                [" owner "] = " runtime "
            }
        };
        var reference = new SecretReference
        {
            Name = new SecretName("primary"),
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
        var diagnostics = SecretDiagnostics.ValidateRecords(
        [
            new SecretRecord
            {
                Descriptor = new SecretDescriptor
                {
                    Name = new SecretName("primary"),
                    Metadata = new Dictionary<string, string>
                    {
                        ["owner"] = "runtime",
                        [" owner "] = "design"
                    }
                },
                Value = new SecretValue("value")
            }
        ]);

        diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == SecretDiagnosticCode.InvalidSecret
            && diagnostic.Metadata["path"] == "secrets[0].descriptor.metadata"
            && diagnostic.Message.Contains("after trimming", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolver_returns_missing_diagnostic_for_unknown_reference()
    {
        var resolver = new InMemorySecretResolver([CreateRecord("primary-token", "runtime-value")]);

        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = new SecretName("secondary-token")
        });

        result.Resolved.ShouldBeFalse();
        result.Diagnostic.ShouldNotBeNull();
        result.Diagnostic.Code.ShouldBe(SecretDiagnosticCode.MissingSecret);
        result.Diagnostic.Severity.ShouldBe(SecretDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Resolver_returns_kind_mismatch_diagnostic()
    {
        var resolver = new InMemorySecretResolver([CreateRecord("primary", "runtime-value", kind: "profile")]);

        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = new SecretName("primary"),
            Kind = "credential"
        });

        result.Resolved.ShouldBeFalse();
        result.Descriptor.ShouldNotBeNull();
        result.Diagnostic.ShouldNotBeNull();
        result.Diagnostic.Code.ShouldBe(SecretDiagnosticCode.KindMismatch);
    }

    [Fact]
    public async Task Resolver_returns_ambiguous_diagnostic_when_version_is_required()
    {
        var resolver = new InMemorySecretResolver(
        [
            CreateRecord("primary", "one", version: "v1"),
            CreateRecord("primary", "two", version: "v2")
        ]);

        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = new SecretName("primary")
        });

        result.Resolved.ShouldBeFalse();
        result.Diagnostic.ShouldNotBeNull();
        result.Diagnostic.Code.ShouldBe(SecretDiagnosticCode.AmbiguousSecret);
    }

    [Fact]
    public async Task Resolver_uses_requested_kind_before_reporting_ambiguity()
    {
        var resolver = new InMemorySecretResolver(
        [
            CreateRecord("primary", "profile-value", version: "v1", kind: "profile"),
            CreateRecord("primary", "credential-value", version: "v2", kind: "credential")
        ]);

        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = new SecretName("primary"),
            Kind = "profile"
        });

        result.Resolved.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Reveal().ShouldBe("profile-value");
    }

    [Fact]
    public async Task Resolver_uses_requested_version()
    {
        var resolver = new InMemorySecretResolver(
        [
            CreateRecord("primary", "one", version: "v1"),
            CreateRecord("primary", "two", version: "v2")
        ]);

        var result = await resolver.ResolveAsync(new SecretReference
        {
            Name = new SecretName("primary"),
            Version = "v2"
        });

        result.Resolved.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Reveal().ShouldBe("two");
    }

    [Fact]
    public void Resolver_rejects_duplicate_records()
    {
        var act = () => new InMemorySecretResolver(
        [
            CreateRecord("primary", "one", version: "v1"),
            CreateRecord("primary", "two", version: "v1")
        ]);

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain(nameof(SecretDiagnosticCode.DuplicateSecret));
    }

    [Fact]
    public void Resolver_rejects_duplicate_records_after_trimming_names()
    {
        var act = () => new InMemorySecretResolver(
        [
            CreateRecord(" primary ", "one", version: "v1"),
            CreateRecord("primary", "two", version: "v1")
        ]);

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain(nameof(SecretDiagnosticCode.DuplicateSecret));
    }

    [Fact]
    public void Resolver_rejects_duplicate_records_after_trimming_versions()
    {
        var act = () => new InMemorySecretResolver(
        [
            CreateRecord("primary", "one", version: " v1 "),
            CreateRecord("primary", "two", version: "v1")
        ]);

        act.ShouldThrow<InvalidOperationException>()
            .Message.ShouldContain(nameof(SecretDiagnosticCode.DuplicateSecret));
    }

    [Fact]
    public void Validation_reports_null_record_entries()
    {
        var diagnostics = SecretDiagnostics.ValidateRecords([null!]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Code.ShouldBe(SecretDiagnosticCode.InvalidSecret);
        diagnostics[0].Severity.ShouldBe(SecretDiagnosticSeverity.Error);
        diagnostics[0].Metadata["path"].ShouldBe("secrets[0]");
        diagnostics[0].Message.ShouldContain("Secret record is required.");
    }

    [Fact]
    public void Resolver_rejects_null_records_with_structured_diagnostic()
    {
        var act = () => new InMemorySecretResolver([null!]);

        var exception = act.ShouldThrow<InvalidOperationException>();
        exception.Message.ShouldContain(nameof(SecretDiagnosticCode.InvalidSecret));
        exception.Message.ShouldContain("Secret record is required.");
    }

    [Fact]
    public void Duplicate_helper_ignores_null_records()
    {
        var diagnostics = SecretDiagnostics.FindDuplicateSecrets(
        [
            null!,
            CreateRecord("primary", "one")
        ]);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Validation_reports_invalid_reference_fields()
    {
        var diagnostics = SecretDiagnostics.ValidateReference(new SecretReference
        {
            Name = default,
            Version = " ",
            Attributes = new Dictionary<string, string>
            {
                [""] = "value",
                ["empty"] = ""
            }
        });

        diagnostics.ShouldContain(diagnostic => diagnostic.Code == SecretDiagnosticCode.InvalidSecret);
        diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains("name", StringComparison.Ordinal));
        diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains("version", StringComparison.Ordinal));
        diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains("Keys", StringComparison.Ordinal));
        diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains("Values", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_reports_null_record_metadata_reference_attributes_and_option_metadata()
    {
        var recordDiagnostics = SecretDiagnostics.ValidateRecords(
        [
            new SecretRecord
            {
                Descriptor = new SecretDescriptor
                {
                    Name = new SecretName("primary"),
                    Metadata = null!
                },
                Value = new SecretValue("value")
            }
        ]);
        var referenceDiagnostics = SecretDiagnostics.ValidateReference(new SecretReference
        {
            Name = new SecretName("primary"),
            Attributes = null!
        });
        var optionDiagnostics = SecretDiagnostics.ValidateOptionReference(new SecretOptionReference
        {
            OptionPath = "credential",
            Metadata = null!
        });

        recordDiagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == SecretDiagnosticCode.InvalidSecret
            && diagnostic.Metadata["path"] == "secrets[0].descriptor.metadata"
            && diagnostic.Message.Contains("Map cannot be null.", StringComparison.Ordinal));
        referenceDiagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == SecretDiagnosticCode.InvalidSecret
            && diagnostic.Metadata["path"] == "reference.attributes"
            && diagnostic.Message.Contains("Map cannot be null.", StringComparison.Ordinal));
        optionDiagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == SecretDiagnosticCode.InvalidSecret
            && diagnostic.Metadata["path"] == "option.metadata"
            && diagnostic.Message.Contains("Map cannot be null.", StringComparison.Ordinal));
    }

    [Fact]
    public void Redactor_hides_sensitive_values()
    {
        var values = new Dictionary<string, string>
        {
            ["name"] = "primary",
            ["accessToken"] = "secret-value",
            ["custom"] = "hidden"
        };

        var redacted = SecretRedactor.RedactValues(values, ["custom"]);

        redacted["name"].ShouldBe("primary");
        redacted["accessToken"].ShouldBe(SecretRedactor.RedactedText);
        redacted["custom"].ShouldBe(SecretRedactor.RedactedText);
    }

    [Theory]
    [InlineData("dbPwd")]
    [InlineData("PassPhrase")]
    [InlineData("authorizationHeader")]
    [InlineData("bearerValue")]
    [InlineData("ConnectionString")]
    [InlineData("clientCertificate")]
    [InlineData("cardPin")]
    [InlineData("hashSalt")]
    public void Redactor_treats_extended_fragments_as_sensitive(string key)
    {
        SecretRedactor.ShouldRedact(key).ShouldBeTrue(key);
    }

    [Fact]
    public void Redactor_handles_null_key_safely()
    {
        SecretRedactor.ShouldRedact(null).ShouldBeFalse();
        SecretRedactor.ShouldRedact(null, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "custom" })
            .ShouldBeFalse();
    }

    [Fact]
    public void SecretValue_to_string_is_always_redacted()
    {
        new SecretValue("raw-secret-value").ToString().ShouldBe("[redacted]");
        SecretRedactor.RedactedText.ShouldBe("[redacted]");
    }

    [Fact]
    public void SecretValue_json_serialization_does_not_emit_raw_value()
    {
        var json = JsonSerializer.Serialize(new SecretValue("raw-secret-value"));

        json.ShouldNotContain("raw-secret-value");
    }

    [Fact]
    public void SecretResolveResult_json_serialization_does_not_emit_raw_value()
    {
        var result = SecretResolveResult.ResolvedResult(
            new SecretReference { Name = new SecretName("primary") },
            new SecretDescriptor { Name = new SecretName("primary") },
            new SecretValue("raw-secret-value"));

        var json = JsonSerializer.Serialize(result);

        json.ShouldNotContain("raw-secret-value");
    }

    [Fact]
    public void Diagnostic_metadata_is_copied_and_null_assignments_become_empty()
    {
        var metadata = new Dictionary<string, string>
        {
            ["path"] = "secrets[0]"
        };

        var diagnostic = new SecretDiagnostic
        {
            Code = SecretDiagnosticCode.InvalidSecret,
            Severity = SecretDiagnosticSeverity.Error,
            Message = "Invalid secret.",
            Metadata = metadata
        };
        var emptyMetadataDiagnostic = new SecretDiagnostic
        {
            Code = SecretDiagnosticCode.MissingSecret,
            Severity = SecretDiagnosticSeverity.Error,
            Message = "Missing secret.",
            Metadata = null!
        };

        metadata["path"] = "changed";

        diagnostic.Metadata["path"].ShouldBe("secrets[0]");
        emptyMetadataDiagnostic.Metadata.ShouldBeEmpty();
    }

    [Fact]
    public void Diagnostic_and_result_formatting_do_not_emit_metadata_values()
    {
        var diagnostic = new SecretDiagnostic
        {
            Code = SecretDiagnosticCode.ResolveFailed,
            Severity = SecretDiagnosticSeverity.Error,
            Message = "Resolution failed.",
            Metadata = new Dictionary<string, string>
            {
                ["accessToken"] = "secret-value"
            }
        };

        var result = SecretResolveResult.Failed(
            new SecretReference { Name = new SecretName("primary") },
            "Resolution failed.");

        diagnostic.ToString().ShouldBe("Error ResolveFailed: Resolution failed.");
        diagnostic.ToString().ShouldNotContain("secret-value");
        result.ToString().ShouldBe("Error ResolveFailed: Resolution failed.");
    }

    [Fact]
    public async Task Unresolved_helper_returns_diagnostics()
    {
        var resolver = new InMemorySecretResolver([CreateRecord("primary", "value", kind: "profile")]);

        var diagnostics = await SecretDiagnostics.FindUnresolvedSecretsAsync(
            resolver,
            [
                new SecretReference { Name = new SecretName("primary"), Kind = "profile" },
                new SecretReference { Name = new SecretName("primary"), Kind = "credential" },
                new SecretReference { Name = new SecretName("secondary") }
            ]);

        diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe(
        [
            SecretDiagnosticCode.KindMismatch,
            SecretDiagnosticCode.MissingSecret
        ]);
    }

    [Fact]
    public async Task Unresolved_helper_reports_null_reference_entries()
    {
        var diagnostics = await SecretDiagnostics.FindUnresolvedSecretsAsync(
            new InMemorySecretResolver([]),
            [null!]);

        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Code.ShouldBe(SecretDiagnosticCode.InvalidSecret);
        diagnostics[0].Metadata["path"].ShouldBe("references[0]");
        diagnostics[0].Message.ShouldContain("Secret reference is required.");
    }

    private static SecretRecord CreateRecord(
        string name,
        string value,
        string? version = null,
        string? kind = null)
        => new()
        {
            Descriptor = new SecretDescriptor
            {
                Name = new SecretName(name),
                Version = version,
                Kind = kind,
                DisplayName = "Primary",
                Metadata = new Dictionary<string, string>
                {
                    ["owner"] = "runtime"
                }
            },
            Value = new SecretValue(value)
        };
}
