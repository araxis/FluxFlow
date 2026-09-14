using System.Threading.Tasks.Dataflow;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentRegistrationTests
{
    [Fact]
    public void Registration_is_lazy_idempotent_and_ordinal()
    {
        var calls = 0;
        Func<ComponentActivationContext, IFlowNode> factory = _ => { calls++; throw new NotSupportedException(); };
        var services = new ServiceCollection();

        services.AddComponent(" node ", factory).ShouldBeSameAs(services);
        services.AddComponent("node", factory);
        using var provider = services.BuildServiceProvider();
        var factories = FluxFlowRegistrationExtensions.CreateFactoryMap(provider);
        factories.Keys.ShouldBe(["node"]);
        factories.ContainsKey("NODE").ShouldBeFalse();
        factories.ContainsKey(" node ").ShouldBeFalse();
        calls.ShouldBe(0);
    }

    [Fact]
    public void Invalid_and_conflicting_registrations_leave_services_unchanged()
    {
        Func<ComponentActivationContext, IFlowNode> factory = _ => throw new NotSupportedException();
        var services = new ServiceCollection().AddComponent("node", factory);
        var before = services.ToArray();
        Should.Throw<InvalidOperationException>(() => services.AddComponent("node", (Func<ComponentActivationContext, IFlowNode>)(_ => throw new Exception("different"))));
        Should.Throw<ArgumentException>(() => services.AddComponent(" ", factory));
        Should.Throw<ArgumentNullException>(() => services.AddComponent("other", (Func<ComponentActivationContext, IFlowNode>)null!));
        Should.Throw<ArgumentNullException>(() => FluxFlowRegistrationExtensions.AddComponent(null!, "node", factory));
        services.ToArray().ShouldBe(before);
    }
}
