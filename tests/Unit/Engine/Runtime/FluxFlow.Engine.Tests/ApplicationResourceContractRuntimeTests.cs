using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationResourceContractRuntimeTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Code_first_resource_contract_executes_from_definition_without_host_registrar()
    {
        var tracker = new ResourceLifetimeTracker();
        var registrar = new PrefixResourceRegistrar();
        var resourceContract = CreateResourceContract(registrar);
        var fixture = Definition(resourceContract, CreateNodeContract(), "embedded");
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddFluxFlow(
            fixture.Definition,
            options => options.StartWithHost = false);
        services.ShouldNotContain(static descriptor =>
            descriptor.ServiceType == typeof(IApplicationResourceRegistrar));
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();

        var started = await application.StartAsync();
        var response = await SendAsync(application.Ports, fixture, "value");

        started.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        registrar.RegistrationCount.ShouldBe(1);
        application.CurrentDefinition.ShouldBeSameAs(fixture.Definition);
        application.CurrentDefinition!.ApplicationResourceContracts
            .ShouldHaveSingleItem().ShouldBeSameAs(resourceContract);
        response.ShouldBe("embedded:value");
        tracker.Created("embedded").ShouldBe(1);
        tracker.Disposed("embedded").ShouldBe(0);

        await application.StopAsync();
        tracker.Disposed("embedded").ShouldBe(1);
    }

    [Fact]
    public async Task Embedded_and_explicit_exact_registrar_identity_runs_once_per_revision()
    {
        var tracker = new ResourceLifetimeTracker();
        var registrar = new PrefixResourceRegistrar();
        var fixture = Definition(
            CreateResourceContract(registrar),
            CreateNodeContract(),
            "deduplicated");
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddApplicationResourceRegistrar(registrar);
        services.AddFluxFlow(
            fixture.Definition,
            options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();

        (await application.StartAsync()).Status.ShouldBe(ApplicationUpdateStatus.Applied);

        registrar.RegistrationCount.ShouldBe(1);
        (await SendAsync(application.Ports, fixture, "once"))
            .ShouldBe("deduplicated:once");
        tracker.Created("deduplicated").ShouldBe(1);

        await application.StopAsync();
        tracker.Disposed("deduplicated").ShouldBe(1);
    }

    [Fact]
    public async Task Hot_reload_adds_reuses_replaces_and_removes_resource_contracts_with_exact_owned_disposal()
    {
        var tracker = new ResourceLifetimeTracker();
        var initialRegistrar = new PrefixResourceRegistrar();
        var replacementRegistrar = new PrefixResourceRegistrar();
        var nodeContract = CreateNodeContract();
        var initialContract = CreateResourceContract(initialRegistrar);
        var initial = Definition(
            initialContract,
            nodeContract,
            "initial");
        var sameIdentity = Definition(
            initialContract,
            nodeContract,
            "initial");
        var replacement = Definition(
            CreateResourceContract(replacementRegistrar),
            nodeContract,
            "replacement");
        var empty = new ApplicationDefinitionBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddFluxFlow(
            empty,
            options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();

        (await application.StartAsync()).Status.ShouldBe(ApplicationUpdateStatus.Unchanged);
        var added = await application.ApplyAsync("resource-addition", initial.Definition);

        added.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        added.PreviousRevision.ShouldBeNull();
        application.CurrentDefinition.ShouldBeSameAs(initial.Definition);
        (await SendAsync(application.Ports, initial, "one")).ShouldBe("initial:one");
        var unchanged = await application.ApplyAsync(
            "rebuilt-same-contract-identity",
            sameIdentity.Definition);

        unchanged.Status.ShouldBe(ApplicationUpdateStatus.Unchanged);
        application.CurrentDefinition.ShouldBeSameAs(sameIdentity.Definition);
        initialRegistrar.RegistrationCount.ShouldBe(1);
        tracker.Created("initial").ShouldBe(1);
        tracker.Disposed("initial").ShouldBe(0);
        var replaced = await application.ApplyAsync("resource-replacement", replacement.Definition);

        replaced.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        replaced.PreviousRevision.ShouldNotBeNull();
        application.CurrentDefinition.ShouldBeSameAs(replacement.Definition);
        replacementRegistrar.RegistrationCount.ShouldBe(1);
        tracker.Created("initial").ShouldBe(1);
        tracker.Disposed("initial").ShouldBe(1);
        tracker.Created("replacement").ShouldBe(1);
        tracker.Disposed("replacement").ShouldBe(0);
        (await SendAsync(application.Ports, replacement, "two"))
            .ShouldBe("replacement:two");

        var removed = await application.ApplyAsync("resource-removal", empty);

        removed.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        application.CurrentDefinition.ShouldBeSameAs(empty);
        application.CurrentDefinition!.ApplicationResourceContracts.ShouldBeEmpty();
        tracker.Disposed("replacement").ShouldBe(1);
        await application.StopAsync();
        tracker.Disposed("replacement").ShouldBe(1);
    }

    [Fact]
    public async Task Failed_resource_contract_revision_preserves_active_resource_factory_route_and_ownership()
    {
        var tracker = new ResourceLifetimeTracker();
        var initialRegistrar = new PrefixResourceRegistrar();
        var candidateRegistrar = new PrefixResourceRegistrar();
        var initial = Definition(
            CreateResourceContract(initialRegistrar),
            CreateNodeContract(),
            "active");
        var candidate = Definition(
            CreateResourceContract(candidateRegistrar),
            CreateNodeContract(failAfterResourceResolution: true),
            "candidate");
        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddFluxFlow(
            initial.Definition,
            options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();

        (await application.StartAsync()).Status.ShouldBe(ApplicationUpdateStatus.Applied);
        var activeRevision = application.Current;
        var activeDefinition = application.CurrentDefinition;
        (await SendAsync(application.Ports, initial, "before")).ShouldBe("active:before");

        var rejected = await application.ApplyAsync("failed-resource", candidate.Definition);

        rejected.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        rejected.ActiveRevision.ShouldBeSameAs(activeRevision);
        rejected.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Error.Details.HasValue &&
            diagnostic.Error.Details.Value.GetProperty("exceptionMessage").GetString()!
                .Contains("candidate activation failed", StringComparison.Ordinal));
        application.Current.ShouldBeSameAs(activeRevision);
        application.CurrentDefinition.ShouldBeSameAs(activeDefinition);
        candidateRegistrar.RegistrationCount.ShouldBe(1);
        tracker.Created("candidate").ShouldBe(1);
        tracker.Disposed("candidate").ShouldBe(1);
        tracker.Disposed("active").ShouldBe(0);
        (await SendAsync(application.Ports, initial, "after")).ShouldBe("active:after");

        await application.StopAsync();
        tracker.Disposed("active").ShouldBe(1);
    }

    [Fact]
    public async Task Successful_resource_contract_replacement_retires_registrar_closure()
    {
        await using var context = RunOnTerminatedThread(
            static () => CreateResourceRetirementContext());

        ForceFullCollection();
        context.Closure.IsAlive.ShouldBeFalse();

        await context.Application.StopAsync();
    }

    [Fact]
    public async Task Json_resource_definition_still_requires_explicit_host_registrar()
    {
        const string json =
            "{\"Resources\":{\"Prefix\":{\"Type\":\"test.prefix-resource\",\"Value\":\"json\"}}," +
            "\"Workflows\":{\"Main\":{\"Worker\":{\"Type\":\"test.resource-node\"," +
            "\"Resource\":\"Resources.Prefix\"}}}}";
        var definition = ApplicationDefinitionJson.Deserialize(json);
        var nodeContract = CreateNodeContract();
        var missingServices = new ServiceCollection();
        missingServices.AddSingleton(new ResourceLifetimeTracker());
        missingServices.AddFluxFlow(
            definition,
            options => options.StartWithHost = false)
            .AddComponent(nodeContract);
        await using var missingProvider = missingServices.BuildServiceProvider();
        var missing = missingProvider.GetRequiredService<FluxFlowApplication>();

        var rejected = await missing.StartAsync();

        rejected.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        rejected.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Error.Details.HasValue &&
            diagnostic.Error.Details.Value.GetProperty("exceptionMessage").GetString()!
                .Contains("no keyed service", StringComparison.OrdinalIgnoreCase));
        missing.CurrentDefinition.ShouldBeNull();
        definition.ApplicationResourceContracts.ShouldBeEmpty();
        await missing.StopAsync();

        var tracker = new ResourceLifetimeTracker();
        var registrar = new PrefixResourceRegistrar();
        var explicitServices = new ServiceCollection();
        explicitServices.AddSingleton(tracker);
        explicitServices.AddApplicationResourceRegistrar(registrar);
        explicitServices.AddFluxFlow(
            definition,
            options => options.StartWithHost = false)
            .AddComponent(nodeContract);
        await using var explicitProvider = explicitServices.BuildServiceProvider();
        var explicitApplication = explicitProvider.GetRequiredService<FluxFlowApplication>();

        var started = await explicitApplication.StartAsync();
        started.Status.ShouldBe(
            ApplicationUpdateStatus.Applied,
            string.Join(
                Environment.NewLine,
                started.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Stage}: {diagnostic.Error.Code}: {diagnostic.Error.Message}: " +
                    $"{diagnostic.Error.Details?.GetRawText()}")));
        var input = ApplicationAddress.WorkflowPort("Main", "Worker", "Input");
        var output = ApplicationAddress.WorkflowPort("Main", "Worker", "Output");
        var receive = explicitApplication.Ports.ReceiveAsync<string>(output, TestTimeout);
        var sent = await explicitApplication.Ports.SendAsync(input, FlowMessage.Create("value"));

        sent.Status.ShouldBe(PortSendStatus.Accepted);
        (await receive).Message!.Value.ShouldBe("json:value");
        registrar.RegistrationCount.ShouldBe(1);
        await explicitApplication.StopAsync();
        tracker.Disposed("json").ShouldBe(1);
    }

    private static ApplicationResourceContract<PrefixResourceOptions, PrefixResourceHandle>
        CreateResourceContract(IApplicationResourceRegistrar registrar)
        => ApplicationResourceContract.Create<PrefixResourceOptions, PrefixResourceHandle>(
            "test.prefix-resource",
            registrar,
            static () => new PrefixResourceOptions(),
            static (options, definition) => definition.Set("Value", options.Value),
            static definition => new PrefixResourceHandle(definition));

    private static ComponentContract<ResourceNodeOptions, InputOutputComponentHandle<string, string>>
        CreateNodeContract(bool failAfterResourceResolution = false)
        => ComponentContract.Create<ResourceNodeOptions, InputOutputComponentHandle<string, string>>(
            "test.resource-node",
            component =>
            {
                component.AddResource<PrefixResource>("Resource", isRequired: true);
                component
                    .UseFactory(context =>
                    {
                        var resource = context.GetRequiredResource<PrefixResource>("Resource");
                        if (failAfterResourceResolution)
                            throw new InvalidOperationException("candidate activation failed");
                        return new PrefixNode(resource.Value);
                    })
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output)
                    .HasEvents("Events", static node => node.Events);
            },
            static () => new ResourceNodeOptions(),
            static (options, definition) =>
                definition.UseResource("Resource", options.Resource!.Definition),
            static component => new InputOutputComponentHandle<string, string>(
                component,
                "Input",
                "Output",
                "Events"));

    private static ResourceDefinitionFixture Definition(
        ApplicationResourceContract<PrefixResourceOptions, PrefixResourceHandle> resourceContract,
        ComponentContract<ResourceNodeOptions, InputOutputComponentHandle<string, string>> nodeContract,
        string value)
    {
        var builder = new ApplicationDefinitionBuilder();
        var resource = builder.AddResource(
            "Prefix",
            resourceContract,
            options => options.Value = value);
        var node = builder.AddWorkflow("Main").AddComponent(
            "Worker",
            nodeContract,
            options => options.Resource = resource);
        return new ResourceDefinitionFixture(builder.Build(), node);
    }

    private static async Task<string> SendAsync(
        ApplicationPorts ports,
        ResourceDefinitionFixture fixture,
        string value)
    {
        var receive = ports.ReceiveAsync(fixture.Node.Output, TestTimeout);
        var sent = await ports.SendAsync(fixture.Node.Input, FlowMessage.Create(value));
        sent.Status.ShouldBe(PortSendStatus.Accepted);
        return (await receive).Message!.Value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LifetimeFixture CreateLifetimeFixture()
    {
        var closure = new RegistrarClosure();
        var contract = CreateResourceContract(new DelegateRegistrar(closure.Register));
        var builder = new ApplicationDefinitionBuilder();
        builder.AddResource("Prefix", contract, static options => options.Value = "captured");
        return new LifetimeFixture(
            new MutableDefinitionSource(builder.Build()),
            new WeakReference(closure));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ResourceRetirementAssertionContext CreateResourceRetirementContext()
    {
        var fixture = CreateLifetimeFixture();
        var services = new ServiceCollection();
        services.AddSingleton(new ResourceLifetimeTracker());
        services.AddFluxFlow(
            fixture.Source,
            options => options.StartWithHost = false);
        var provider = services.BuildServiceProvider();

        try
        {
            var application = provider.GetRequiredService<FluxFlowApplication>();
            application.StartAsync().GetAwaiter().GetResult().Status
                .ShouldBe(ApplicationUpdateStatus.Applied);
            fixture.Closure.IsAlive.ShouldBeTrue();
            ReplaceLifetimeRevisionAsync(application, fixture.Source).GetAwaiter().GetResult();
            return new ResourceRetirementAssertionContext(
                provider,
                application,
                fixture.Closure);
        }
        catch
        {
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ReplaceLifetimeRevisionAsync(
        FluxFlowApplication application,
        MutableDefinitionSource source)
    {
        var builder = new ApplicationDefinitionBuilder();
        builder.AddResource(
            "Prefix",
            CreateResourceContract(new PrefixResourceRegistrar()),
            static options => options.Value = "replacement");
        source.Definition = builder.Build();

        var replacement = await application.ReloadAsync("resource-contract-replacement");

        replacement.Status.ShouldBe(ApplicationUpdateStatus.Applied);
        replacement.PreviousRevision.ShouldNotBeNull();
        application.CurrentDefinition.ShouldBeSameAs(source.Definition);
        application.LastUpdate!.PreviousRevision.ShouldBeNull();
    }

    private static T RunOnTerminatedThread<T>(Func<T> operation)
        where T : class
    {
        T? result = null;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = operation();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        thread.Join();
        failure?.Throw();
        return result ?? throw new InvalidOperationException(
            "The resource retirement assertion thread completed without a result.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceFullCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static IEnumerable<(ApplicationAddress Address, ResourceInstanceDefinition Definition)>
        FlattenResources(
            IReadOnlyDictionary<string, ResourceDefinition> resources,
            IReadOnlyList<string>? path = null)
    {
        path ??= [];
        foreach (var (name, resource) in resources)
        {
            string[] next = [.. path, name];
            if (resource is ResourceGroupDefinition group)
            {
                foreach (var nested in FlattenResources(group.Resources, next))
                    yield return nested;
            }
            else
            {
                yield return (
                    ApplicationAddress.Resource(next),
                    (ResourceInstanceDefinition)resource);
            }
        }
    }

    private sealed record ResourceDefinitionFixture(
        ApplicationDefinition Definition,
        InputOutputComponentHandle<string, string> Node);

    private sealed class PrefixResourceOptions
    {
        public string Value { get; set; } = "default";
    }

    private sealed class ResourceNodeOptions
    {
        public PrefixResourceHandle? Resource { get; set; }
    }

    private sealed class PrefixResourceHandle(ResourceHandle definition)
        : AuthoredResourceHandle(definition);

    private sealed class PrefixResource(string value, ResourceLifetimeTracker tracker)
        : IDisposable
    {
        private int _disposed;

        public string Value { get; } = value;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                tracker.RecordDisposed(Value);
        }
    }

    private sealed class PrefixResourceRegistrar : IApplicationResourceRegistrar
    {
        public int RegistrationCount { get; private set; }

        public void Register(ApplicationResourceRegistrationContext context)
        {
            RegistrationCount++;
            var tracker = context.HostServices.GetRequiredService<ResourceLifetimeTracker>();
            foreach (var (address, resource) in FlattenResources(context.Definition.Resources))
            {
                if (!string.Equals(resource.Type, "test.prefix-resource", StringComparison.Ordinal))
                    continue;

                var value = resource.Properties["Value"].GetString()!;
                context.Services.AddFluxFlowResource<PrefixResource>(
                    address,
                    _ =>
                    {
                        tracker.RecordCreated(value);
                        return new PrefixResource(value, tracker);
                    });
            }
        }
    }

    private sealed class DelegateRegistrar(Action<ApplicationResourceRegistrationContext> register)
        : IApplicationResourceRegistrar
    {
        public void Register(ApplicationResourceRegistrationContext context) => register(context);
    }

    private sealed class RegistrarClosure
    {
        public void Register(ApplicationResourceRegistrationContext context)
        {
            var tracker = context.HostServices.GetRequiredService<ResourceLifetimeTracker>();
            foreach (var (address, resource) in FlattenResources(context.Definition.Resources))
            {
                if (!string.Equals(resource.Type, "test.prefix-resource", StringComparison.Ordinal))
                    continue;
                var value = resource.Properties["Value"].GetString()!;
                context.Services.AddFluxFlowResource<PrefixResource>(
                    address,
                    _ =>
                    {
                        tracker.RecordCreated(value);
                        return new PrefixResource(value, tracker);
                    });
            }
        }
    }

    private sealed class ResourceLifetimeTracker
    {
        private readonly ConcurrentDictionary<string, int> _created =
            new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _disposed =
            new(StringComparer.Ordinal);

        public int Created(string value) => _created.GetValueOrDefault(value);

        public int Disposed(string value) => _disposed.GetValueOrDefault(value);

        public void RecordCreated(string value) => _created.AddOrUpdate(value, 1, static (_, count) => count + 1);

        public void RecordDisposed(string value) => _disposed.AddOrUpdate(value, 1, static (_, count) => count + 1);
    }

    private sealed class PrefixNode(string prefix) : FlowNode<string, string>
    {
        protected override async Task ProcessAsync(FlowMessage<string> message)
            => await EmitAsync(message.With($"{prefix}:{message.Value}"), Stopping)
                .ConfigureAwait(false);
    }

    private sealed class MutableDefinitionSource(ApplicationDefinition definition)
        : IApplicationDefinitionSource
    {
        public ApplicationDefinition Definition { get; set; } = definition;

        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Definition);
        }
    }

    private sealed record LifetimeFixture(MutableDefinitionSource Source, WeakReference Closure);

    private sealed class ResourceRetirementAssertionContext(
        ServiceProvider provider,
        FluxFlowApplication application,
        WeakReference closure) : IAsyncDisposable
    {
        public FluxFlowApplication Application { get; } = application;

        public WeakReference Closure { get; } = closure;

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
