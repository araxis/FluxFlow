using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Definition;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Json;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ContextComponentRegistrationTests
{
    [Fact]
    public async Task Factories_are_lazy_and_each_instance_declares_its_own_address_names()
    {
        var calls = 0;
        var services = new ServiceCollection();
        Func<ComponentActivationContext, IFlowNode> factory = context =>
        {
            calls++;
            var node = new Echo();
            context.HasInput(context.GetConfigurationValue<string>("inputName")!, node.Input);
            context.HasOutput(node.Output);
            context.HasEvents(node.Events);
            return node;
        };
        services.AddComponent("echo", factory).ShouldBeSameAs(services);
        services.AddComponent("echo", factory);
        calls.ShouldBe(0);
        await using var provider = services.BuildServiceProvider();
        var definition = ApplicationDefinitionJson.Deserialize("""
            {"Workflows":{"main":{
              "first":{"Type":"echo","inputName":"Start","Output":"second.Next"},
              "second":{"Type":"echo","inputName":"Next"}
            }}}
            """);
        await using var application = await definition.BuildApplicationAsync(provider);
        calls.ShouldBe(2);
        await application.StartAsync();
        var reply = await application.RequestAsync("main.first.Start", "main.second.Output",
            FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), TimeSpan.FromSeconds(5));
        reply.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(42);
        await application.StopAsync();
    }

    [Theory]
    [InlineData("duplicate", false)]
    [InlineData("duplicate", true)]
    [InlineData("null", false)]
    [InlineData("events", false)]
    public async Task Invalid_bindings_dispose_the_returned_node_and_preserve_cleanup_failures(string invalidity, bool cleanupFails)
    {
        var cleanup = new InvalidOperationException("cleanup failed");
        var node = new Echo(cleanupFails ? cleanup : null);
        await using var provider = new ServiceCollection().AddComponent("echo", context =>
        {
            context.HasInput(node.Input);
            // Deferred until the factory returns its owned node.
            if (invalidity == "duplicate") context.HasInput(node.Input);
            if (invalidity == "null") context.HasOutput(null!);
            if (invalidity == "events")
            {
                context.HasOutput(node.Output);
                context.HasEvents("Output", node.Events);
            }
            return node;
        }).BuildServiceProvider();
        var error = await Record.ExceptionAsync(() => Single().BuildApplicationAsync(provider));
        error.ShouldNotBeNull();
        node.Disposals.ShouldBe(1);
        if (cleanupFails) error.ToString().ShouldContain("cleanup failed");
    }

    [Fact]
    public async Task Invalid_links_release_all_created_nodes_before_processing_starts()
    {
        var nodes = new List<Echo>();
        await using var provider = new ServiceCollection().AddComponent("echo", context =>
        {
            var node = new Echo();
            nodes.Add(node);
            context.HasInput(node.Input);
            context.HasOutput(node.Output);
            return node;
        }).BuildServiceProvider();
        var definition = ApplicationDefinitionJson.Deserialize("""
            {"Workflows":{"main":{
              "first":{"Type":"echo","Output":"second.Missing"},
              "second":{"Type":"echo"}
            }}}
            """);
        var error = await Should.ThrowAsync<InvalidOperationException>(() => definition.BuildApplicationAsync(provider));
        error.Message.ShouldContain("Missing");
        nodes.Count.ShouldBe(2);
        nodes.ShouldAllBe(node => node.Disposals == 1 && node.Calls == 0);
    }

    [Fact]
    public void A_conflicting_factory_does_not_replace_the_original()
    {
        var services = new ServiceCollection();
        services.AddComponent("echo", _ => new Echo());
        var count = services.Count;
        Should.Throw<InvalidOperationException>(() => services.AddComponent("echo", _ => new Echo()));
        services.Count.ShouldBe(count);
    }

    [Fact]
    public async Task Reusing_an_owned_node_does_not_dispose_the_running_application()
    {
        var node = new Echo();
        await using var provider = new ServiceCollection().AddComponent("echo", context =>
        {
            context.HasInput(node.Input);
            context.HasOutput(node.Output);
            return node;
        }).BuildServiceProvider();
        await using var first = await Single().BuildApplicationAsync(provider);
        await first.StartAsync();
        await Should.ThrowAsync<InvalidOperationException>(() => Single().BuildApplicationAsync(provider));
        node.Disposals.ShouldBe(0);
        var reply = await first.RequestAsync("main.echo.Input", "main.echo.Output",
            FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(5, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), TimeSpan.FromSeconds(5));
        reply.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(5);
        await first.StopAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sync_and_async_registrations_cannot_claim_the_same_type(bool directFirst)
    {
        var services = new ServiceCollection();
        Func<ComponentActivationContext, ValueTask<IFlowNode>> other = _ => ValueTask.FromResult<IFlowNode>(new Echo());
        if (directFirst) services.AddComponent("echo", _ => new Echo());
        else services.AddComponent("echo", other);
        var count = services.Count;
        Should.Throw<InvalidOperationException>(() =>
        {
            if (directFirst) services.AddComponent("echo", other);
            else services.AddComponent("echo", _ => new Echo());
        });
        services.Count.ShouldBe(count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Async_factories_are_lazy_and_release_owned_resources(bool invalidPorts)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = new Echo();
        var disposals = 0;
        Func<ComponentActivationContext, ValueTask<IFlowNode>> factory = async context =>
        {
            entered.SetResult();
            await release.Task;
            context.HasInput(node.Input);
            context.HasOutput(node.Output);
            context.SetLifetime(disposeAsync: () => { disposals++; return ValueTask.CompletedTask; });
            if (invalidPorts) context.HasInput(node.Input);
            return node;
        };
        var services = new ServiceCollection();
        services.AddComponent("echo", factory).AddComponent("echo", factory);
        entered.Task.IsCompleted.ShouldBeFalse();
        await using var provider = services.BuildServiceProvider();
        var build = Single().BuildApplicationAsync(provider);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        build.IsCompleted.ShouldBeFalse();
        release.SetResult();
        if (invalidPorts)
            (await Should.ThrowAsync<InvalidOperationException>(() => build)).InnerException.ShouldBeOfType<ArgumentException>();
        else
        {
            await using var app = await build;
            await app.StartAsync();
            var reply = await app.RequestAsync("main.echo.Input", "main.echo.Output",
                FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(17, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), TimeSpan.FromSeconds(5));
            reply.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(17);
            await app.StopAsync();
        }
        node.Disposals.ShouldBe(1);
        disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Additional_cleanup_runs_even_when_node_cleanup_fails()
    {
        var nodeFailure = new InvalidOperationException("node cleanup");
        var ownerFailure = new InvalidOperationException("owner cleanup");
        await using var provider = new ServiceCollection().AddComponent("echo", async context =>
        {
            await Task.Yield();
            var node = new Echo(nodeFailure);
            context.HasInput(node.Input);
            context.HasInput(node.Input);
            context.SetLifetime(disposeAsync: () => ValueTask.FromException(ownerFailure));
            return node;
        }).BuildServiceProvider();
        var failure = await Should.ThrowAsync<InvalidOperationException>(() => Single().BuildApplicationAsync(provider));
        var error = failure.InnerException.ShouldBeOfType<AggregateException>();
        error.Flatten().InnerExceptions.ShouldContain(nodeFailure);
        error.Flatten().InnerExceptions.ShouldContain(ownerFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sync_and_async_factories_cannot_claim_the_same_type(bool asyncFirst)
    {
        var services = new ServiceCollection();
        Func<ComponentActivationContext, ValueTask<IFlowNode>> asyncFactory = _ => ValueTask.FromResult<IFlowNode>(new Echo());
        Func<ComponentActivationContext, IFlowNode> syncFactory = _ => new Echo();
        if (asyncFirst) services.AddComponent("echo", asyncFactory);
        else services.AddComponent("echo", syncFactory);
        var count = services.Count;
        Should.Throw<InvalidOperationException>(() =>
        {
            if (asyncFirst) services.AddComponent("echo", syncFactory);
            else services.AddComponent("echo", asyncFactory);
        });
        services.Count.ShouldBe(count);
    }

    [Fact]
    public async Task Direct_instance_uses_explicit_completion_task()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = new Echo();
        var context = new ComponentActivationContext(new ServiceCollection().BuildServiceProvider(),
            "main", "echo", new ComponentDefinition("echo"));
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.SetLifetime(completion: completion.Task);
        var instance = await context.BindAsync(node);
        instance.Completion.ShouldBeSameAs(completion.Task);
        await instance.DisposeAsync();
        completion.SetResult();
    }

    [Fact]
    public void Failed_definition_configuration_does_not_register_a_factory_or_component()
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("main");
        Func<ComponentActivationContext, ValueTask<IFlowNode>> rejected = _ => throw new NotSupportedException();
        Func<ComponentActivationContext, ValueTask<IFlowNode>> accepted = _ => ValueTask.FromResult<IFlowNode>(new Echo());
        Should.Throw<InvalidOperationException>(() => workflow.AddComponent("echo", "echo", rejected,
            _ => throw new InvalidOperationException("configuration"), c => c));
        workflow.AddComponent("echo", "echo", accepted, _ => { }, c => c);
        var setup = builder.Build();
        setup.ComponentFactories["echo"].ShouldBe(accepted);
        setup.Definition.Workflows["main"].Components.Count.ShouldBe(1);
        Should.Throw<NotSupportedException>(() => setup.ToDefinition());
    }

    [Fact]
    public async Task Conflicting_embedded_and_DI_factories_fail_before_activation()
    {
        var builder = new ApplicationDefinitionBuilder();
        builder.AddWorkflow("main").AddComponent("echo", "echo",
            _ => ValueTask.FromResult<IFlowNode>(new Echo()), _ => { }, c => c);
        var calls = 0;
        Func<ComponentActivationContext, ValueTask<IFlowNode>> other = _ =>
        {
            calls++;
            return ValueTask.FromResult<IFlowNode>(new Echo());
        };
        await using var provider = new ServiceCollection().AddComponent("echo", other).BuildServiceProvider();
        await Should.ThrowAsync<InvalidOperationException>(() => builder.Build().BuildApplicationAsync(provider));
        calls.ShouldBe(0);
    }

    [Fact]
    public void Application_setup_snapshots_factories_and_rejects_mutation()
    {
        Func<ComponentActivationContext, ValueTask<IFlowNode>> factory = _ => throw new NotSupportedException();
        var entries = new Dictionary<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>> { ["echo"] = factory };
        var setup = new ApplicationSetup(Single(), componentFactories: entries);
        entries.Clear();
        setup.ComponentFactories["echo"].ShouldBe(factory);
        Should.Throw<NotSupportedException>(() =>
            ((IDictionary<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>)setup.ComponentFactories).Clear());
    }

    private static FluxFlow.Json.Model.ApplicationDefinition Single()
        => ApplicationDefinitionJson.Deserialize("""{"Workflows":{"main":{"echo":{"Type":"echo"}}}}""");

    private sealed class Echo(Exception? cleanup = null) : FlowNode
    {
        public int Disposals { get; private set; }
        public int Calls { get; private set; }
        protected override Task ProcessAsync(FlowMessage message)
        {
            Calls++;
            return EmitAsync(message, Stopping).AsTask();
        }
        protected override ValueTask OnDisposeAsync()
        {
            Disposals++;
            if (cleanup is not null) throw cleanup;
            return ValueTask.CompletedTask;
        }
    }
}
