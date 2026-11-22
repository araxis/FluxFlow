using FluxFlow.Application.Addressing;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentOwnershipTests
{
    [Fact]
    public async Task DisposeAsync_preserves_node_failure_and_calls_its_owned_cleanup()
    {
        var nodeException = new InvalidOperationException("node dispose failed");
        var cleanupCalled = false;
        var node = new ThrowingDisposeNode(nodeException, () => cleanupCalled = true);
        await using var provider = Provider(node);
        var app = await provider.BuildApplicationAsync();
        (await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask())).GetBaseException().ShouldBeSameAs(nodeException);
        cleanupCalled.ShouldBeTrue();
        var exception = await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask());
        exception.GetBaseException().ShouldBeSameAs(nodeException);
        cleanupCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_preserves_all_failures_reported_by_the_node()
    {
        var nodeException = new InvalidOperationException("node dispose failed");
        var hookException = new InvalidOperationException("cleanup hook failed");
        await using var provider = Provider(new ThrowingDisposeNode(new AggregateException(nodeException, hookException)));
        var app = await provider.BuildApplicationAsync();
        (await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask())).Flatten().InnerExceptions.ShouldBe([nodeException, hookException]);
        var exception = await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask());

        exception.Flatten().InnerExceptions.Count.ShouldBe(2);
        ReferenceEquals(exception.Flatten().InnerExceptions[0], nodeException).ShouldBeTrue();
        ReferenceEquals(exception.Flatten().InnerExceptions[1], hookException).ShouldBeTrue();
    }

    [Fact]
    public void Composition_cannot_override_node_lifetime()
    {
        var assembly = typeof(IFlowNode).Assembly;
        assembly.GetType("FluxFlow.Core.Configuration.Factories.ComponentActivationContext").ShouldBeNull();
        assembly.GetType("FluxFlow.Application.ComponentInstance").ShouldBeNull();
    }

    private static ServiceProvider Provider(IFlowNode node)
    {
        var services = new ServiceCollection();
        services.AddComponentFactory(ComponentAddress.Parse("main.node"), _ => node);
        return services.BuildServiceProvider();
    }

    private sealed class ThrowingDisposeNode(Exception disposeException, Action? cleanup = null) : IFlowNode
    {
        public Task Completion { get; } = Task.CompletedTask;

        public void Complete()
        {
        }

        public void Fault(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
        }

        public ValueTask DisposeAsync()
        {
            try { throw disposeException; }
            finally { cleanup?.Invoke(); }
        }
    }
}
