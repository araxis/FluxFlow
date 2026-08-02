using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.TSql.Tests;

public sealed class TSqlDurableInputPackageBoundaryTests
{
    [Fact]
    public void Provider_exposes_only_the_five_durable_input_capabilities_and_no_hosted_service()
    {
        var storeType = typeof(TSqlDurableInputStore);
        var capabilityInterfaces = storeType.GetInterfaces()
            .Where(type => type.Namespace == typeof(IDurableInputStore).Namespace)
            .OrderBy(type => type.Name)
            .ToArray();

        capabilityInterfaces.ShouldBe([
            typeof(IDurableInputDeadLetterStore),
            typeof(IDurableInputLeaseRenewalStore),
            typeof(IDurableInputRetentionStore),
            typeof(IDurableInputStatusStore),
            typeof(IDurableInputStore)
        ], ignoreOrder: true);
        storeType.GetInterfaces().ShouldNotContain(type =>
            type.FullName == "Microsoft.Extensions.Hosting.IHostedService");
    }

    [Fact]
    public void Provider_does_not_reference_other_persistence_providers_or_orms()
    {
        var references = typeof(TSqlDurableInputStore).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        references.ShouldNotContain(name => name.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase));
        references.ShouldNotContain(name => name.Contains("Dapper", StringComparison.OrdinalIgnoreCase));
        references.ShouldNotContain(name => name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
        references.ShouldNotContain(name => name.Contains("DurableOutput", StringComparison.OrdinalIgnoreCase));
    }
}
