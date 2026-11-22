using FluxFlow.Application;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class ResourceResolutionTests
{
    [Theory]
    [InlineData("Client", "Resources.Client")]
    [InlineData("Mqtt.Client", "Resources.Mqtt.Client")]
    [InlineData("resources.Mqtt.Client", "Resources.Mqtt.Client")]
    [InlineData("Resources.Mqtt.Client", "Resources.Mqtt.Client")]
    public void References_resolve_exact_addresses_without_searching_other_branches(string reference, string key)
    {
        var value = new object();
        var services = new ServiceCollection();
        services.AddKeyedSingleton(key, value);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredFlowResource<object>(reference, "main").ShouldBeSameAs(value);

        var missing = new ServiceCollection();
        missing.AddKeyedSingleton("Resources.other.Client", value);
        using var missingProvider = missing.BuildServiceProvider();
        var error = Should.Throw<InvalidOperationException>(() => missingProvider.GetRequiredFlowResource<object>(reference, "main"));
        error.Message.ShouldContain(reference);
    }

    [Fact]
    public void Relative_references_prefer_workflow_then_application_then_host_key()
    {
        var services = new ServiceCollection();
        var local = new object();
        var shared = new object();
        var host = new object();
        services.AddKeyedSingleton("main.Resources.Client", local);
        services.AddKeyedSingleton("Resources.Client", shared);
        services.AddKeyedSingleton("Client", host);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredFlowResource<object>("Client", "main").ShouldBeSameAs(local);
        provider.GetRequiredFlowResource<object>("Client", "other").ShouldBeSameAs(shared);
        provider.GetRequiredFlowResource<object>("Resources.Client", "main").ShouldBeSameAs(shared);
        provider.GetRequiredFlowResource<object>("main.Resources.Client", "other").ShouldBeSameAs(local);
        using var hostProvider = new ServiceCollection().AddKeyedSingleton("Client", host).BuildServiceProvider();
        hostProvider.GetRequiredFlowResource<object>("Client", "main").ShouldBeSameAs(host);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Required_references_reject_missing_values(string? reference)
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        Should.Throw<ArgumentException>(() => provider.GetRequiredFlowResource<object>(reference!));
    }
}
