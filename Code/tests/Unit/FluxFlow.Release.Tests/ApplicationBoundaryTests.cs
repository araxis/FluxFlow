using System.Runtime.CompilerServices;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Application.Execution;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

public sealed class ApplicationBoundaryTests
{
    [Fact]
    public void Core_does_not_depend_on_application_DI_or_hosting()
    {
        var core = typeof(FlowMessage).Assembly;
        core.GetReferencedAssemblies().ShouldNotContain(reference =>
            reference.Name == "FluxFlow.Application" ||
            reference.Name!.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal) ||
            reference.Name.StartsWith("Microsoft.Extensions.Hosting", StringComparison.Ordinal));
        core.GetTypes().ShouldNotContain(type =>
            type.Namespace != null && (type.Namespace.StartsWith("FluxFlow.Core.Application", StringComparison.Ordinal) ||
            type.Namespace.StartsWith("FluxFlow.Core.Addressing", StringComparison.Ordinal) ||
            type.Namespace.StartsWith("FluxFlow.Core.Execution", StringComparison.Ordinal)));
        core.GetCustomAttributes(typeof(InternalsVisibleToAttribute), false)
            .Cast<InternalsVisibleToAttribute>().ShouldNotContain(friend => friend.AssemblyName == "FluxFlow.Application");
    }

    [Fact]
    public void Application_owns_composition_addresses_and_links_without_configuration()
    {
        var application = typeof(FlowApplication).Assembly;
        application.GetName().Name.ShouldBe("FluxFlow.Application");
        typeof(ApplicationAddress).Assembly.ShouldBeSameAs(application);
        typeof(ApplicationAddressKind).Assembly.ShouldBeSameAs(application);
        typeof(FlowLink).Assembly.ShouldBeSameAs(application);
        application.GetReferencedAssemblies().ShouldContain(reference => reference.Name == "FluxFlow.Core");
        application.GetReferencedAssemblies().ShouldNotContain(reference => reference.Name == "FluxFlow.Configuration");
        application.GetCustomAttributes(typeof(InternalsVisibleToAttribute), false)
            .Cast<InternalsVisibleToAttribute>().ShouldNotContain(friend => friend.AssemblyName == "FluxFlow.Configuration");
    }

    [Fact]
    public void Application_does_not_export_revision_or_reload_infrastructure()
    {
        var types = typeof(FlowApplication).Assembly.GetExportedTypes();
        types.ShouldNotContain(type => type.Name.Contains("Revision", StringComparison.Ordinal));
        types.SelectMany(type => type.GetMethods())
            .ShouldNotContain(method => method.Name == "AddFluxFlowUpdates" || method.Name == "ReloadAsync" || method.Name == "ApplyAsync");
    }
}
