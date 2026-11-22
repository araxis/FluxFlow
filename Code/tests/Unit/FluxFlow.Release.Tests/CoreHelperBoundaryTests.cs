using FluxFlow.Core.Mapping;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class CoreHelperBoundaryTests
{
    [Fact]
    public void Mapping_contracts_ship_in_core_without_forwarding_packages()
    {
        typeof(IFlowExpressionEngine).Assembly.ShouldBeSameAs(typeof(FluxFlow.Core.Messaging.FlowMessage).Assembly);
        var packages = PackageManifest.Read(ReleaseTestPaths.FindRepositoryRoot());
        packages.ShouldNotContain(entry => entry.PackageId == "FluxFlow.Mapping" || entry.PackageId == "FluxFlow.Resilience");
    }
}
