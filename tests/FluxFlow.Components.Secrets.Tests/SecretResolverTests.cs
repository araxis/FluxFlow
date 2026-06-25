using FluxFlow.Components.Secrets.Contracts;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace FluxFlow.Components.Secrets.Tests;

public sealed class SecretResolverTests
{
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
