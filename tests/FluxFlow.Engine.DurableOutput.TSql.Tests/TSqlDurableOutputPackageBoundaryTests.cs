using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.Tests;

public sealed class TSqlDurableOutputPackageBoundaryTests
{
    [Fact]
    public void Provider_exposes_only_the_five_durable_output_capabilities_and_no_hosted_service()
    {
        var storeType = typeof(TSqlDurableOutputStore);
        var capabilityInterfaces = storeType.GetInterfaces()
            .Where(type => type.Namespace == typeof(IDurableOutputStore).Namespace)
            .OrderBy(type => type.Name)
            .ToArray();

        capabilityInterfaces.ShouldBe([
            typeof(IDurableOutputDeadLetterStore),
            typeof(IDurableOutputDeliveryStore),
            typeof(IDurableOutputRetentionStore),
            typeof(IDurableOutputStatusStore),
            typeof(IDurableOutputStore)
        ], ignoreOrder: true);
        typeof(IDurableOutputDeliveryStore).GetMethods()
            .Select(static method => method.Name)
            .ShouldBe(
                ["TryLeaseAsync", "RenewLeaseAsync", "CompleteAsync", "RetryAsync", "DeadLetterAsync"],
                ignoreOrder: true);
        storeType.GetInterfaces().ShouldNotContain(type =>
            type.FullName == "Microsoft.Extensions.Hosting.IHostedService");
    }

    [Fact]
    public void Provider_does_not_reference_other_persistence_providers_or_orms()
    {
        var references = typeof(TSqlDurableOutputStore).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        references.ShouldNotContain(name =>
            name.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase));
        references.ShouldNotContain(name =>
            name.Contains("Dapper", StringComparison.OrdinalIgnoreCase));
        references.ShouldNotContain(name =>
            name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
        references.ShouldNotContain(name =>
            name.Contains("DurableInput", StringComparison.OrdinalIgnoreCase));
    }
}
