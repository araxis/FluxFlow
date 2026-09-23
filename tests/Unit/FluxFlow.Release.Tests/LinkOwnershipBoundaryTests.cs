using Shouldly;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class LinkOwnershipBoundaryTests
{
    [Fact]
    public void Core_does_not_grant_Configuration_access_to_internals()
    {
        var assembly = typeof(FluxFlow.Core.Application.FlowApplication).Assembly;
        assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => new AssemblyName(attribute.AssemblyName).Name)
            .ShouldNotContain("FluxFlow.Configuration");
    }

    [Fact]
    public void Core_does_not_export_revision_or_reload_infrastructure()
    {
        var assembly = typeof(FluxFlow.Core.Application.FlowApplication).Assembly;
        assembly.GetExportedTypes().Any(type =>
            type.Namespace?.StartsWith("FluxFlow.Core.Runtime", StringComparison.Ordinal) == true ||
            type.Name.Contains("Revision", StringComparison.Ordinal)).ShouldBeFalse();
        assembly.GetExportedTypes().SelectMany(type => type.GetMethods())
            .Any(method => method.Name is "AddFluxFlowUpdates" or "ReloadAsync" or "ApplyAsync").ShouldBeFalse();
    }

}
