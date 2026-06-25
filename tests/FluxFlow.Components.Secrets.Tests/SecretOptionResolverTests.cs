using FluxFlow.Components.Secrets.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Secrets.Tests;

public sealed class SecretOptionResolverTests
{
    [Fact]
    public async Task ResolveRequired_returns_resolved_value_for_option_reference()
    {
        var resolver = new InMemorySecretResolver([CreateRecord("primary", "runtime-value", kind: "profile")]);
        var reference = new SecretReference
        {
            Name = new SecretName("primary"),
            Kind = "profile"
        };

        var result = await SecretOptionResolver.ResolveRequiredAsync(resolver, reference, "credential");

        result.Resolved.ShouldBeTrue();
        result.OptionPath.ShouldBe("credential");
        result.Value.ShouldNotBeNull();
        result.Value.Reveal().ShouldBe("runtime-value");
        result.ToString().ShouldBe("Resolved secret option 'credential'.");
    }

    [Fact]
    public async Task ResolveRequired_trims_option_path()
    {
        var resolver = new InMemorySecretResolver([CreateRecord("primary", "runtime-value")]);
        var reference = new SecretReference
        {
            Name = new SecretName("primary")
        };

        var result = await SecretOptionResolver.ResolveRequiredAsync(resolver, reference, " credential ");

        result.Resolved.ShouldBeTrue();
        result.OptionPath.ShouldBe("credential");
        result.ToString().ShouldBe("Resolved secret option 'credential'.");
    }

    [Fact]
    public async Task ResolveRequired_returns_diagnostic_when_reference_is_missing()
    {
        var resolver = new InMemorySecretResolver([CreateRecord("primary", "runtime-value")]);

        var result = await SecretOptionResolver.ResolveRequiredAsync(resolver, reference: null, "credential");

        result.Resolved.ShouldBeFalse();
        result.Diagnostic.ShouldNotBeNull();
        result.Diagnostic.Code.ShouldBe(SecretDiagnosticCode.MissingSecretReference);
        result.Diagnostic.Metadata["path"].ShouldBe("credential");
    }

    [Fact]
    public async Task ResolveOptional_allows_absent_reference()
    {
        var resolver = new InMemorySecretResolver([CreateRecord("primary", "runtime-value")]);

        var result = await SecretOptionResolver.ResolveOptionalAsync(resolver, reference: null, "credential");

        result.NotProvided.ShouldBeTrue();
        result.Diagnostic.ShouldBeNull();
        result.ToString().ShouldBe("Secret option 'credential' was not provided.");
    }

    [Fact]
    public async Task ResolveAll_preserves_order_and_result_kinds()
    {
        var resolver = new InMemorySecretResolver([CreateRecord("primary", "runtime-value")]);

        var results = await SecretOptionResolver.ResolveAllAsync(
            resolver,
            [
                new SecretOptionReference
                {
                    OptionPath = "first",
                    Reference = new SecretReference { Name = new SecretName("primary") }
                },
                new SecretOptionReference
                {
                    OptionPath = "second",
                    Reference = new SecretReference { Name = new SecretName("missing") }
                },
                new SecretOptionReference
                {
                    OptionPath = "third",
                    Required = false
                }
            ]);

        results.Select(result => result.OptionPath).ShouldBe(["first", "second", "third"]);
        results[0].Resolved.ShouldBeTrue();
        var missingDiagnostic = results[1].Diagnostic.ShouldNotBeNull();
        missingDiagnostic.Code.ShouldBe(SecretDiagnosticCode.MissingSecret);
        results[2].NotProvided.ShouldBeTrue();
    }

    [Fact]
    public async Task ResolveAll_rejects_null_resolver_even_when_options_are_empty()
    {
        var act = async () => await SecretOptionResolver.ResolveAllAsync(
            null!,
            []);

        await act.ShouldThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_returns_invalid_option_diagnostic_before_resolving()
    {
        var resolver = new CountingSecretResolver();

        var result = await SecretOptionResolver.ResolveAsync(
            resolver,
            new SecretOptionReference
            {
                OptionPath = " ",
                Reference = new SecretReference { Name = default }
            });

        result.Diagnostic.ShouldNotBeNull();
        result.Diagnostic.Code.ShouldBe(SecretDiagnosticCode.InvalidSecret);
        resolver.ResolveCount.ShouldBe(0);
    }

    [Fact]
    public void ValidateOptionReference_includes_option_path_for_reference_diagnostics()
    {
        var diagnostics = SecretDiagnostics.ValidateOptionReference(new SecretOptionReference
        {
            OptionPath = " credential ",
            Reference = new SecretReference { Name = default }
        });

        diagnostics.ShouldContain(diagnostic => diagnostic.Code == SecretDiagnosticCode.InvalidSecret);
        diagnostics.ShouldAllBe(diagnostic => diagnostic.Metadata.ContainsKey("path"));
        diagnostics.Select(diagnostic => diagnostic.Metadata["path"]).ShouldAllBe(path => path == "credential");
        diagnostics.Select(diagnostic => diagnostic.Metadata["optionPath"]).ShouldAllBe(path => path == "credential");
        diagnostics.ShouldContain(diagnostic => diagnostic.Metadata["referencePath"] == "reference.name");
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
                DisplayName = "Primary"
            },
            Value = new SecretValue(value)
        };

    private sealed class CountingSecretResolver : ISecretResolver
    {
        public int ResolveCount { get; private set; }

        public ValueTask<SecretResolveResult> ResolveAsync(
            SecretReference reference,
            CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            return ValueTask.FromResult(SecretResolveResult.Missing(reference));
        }
    }
}
