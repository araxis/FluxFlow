using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Hosting.Snapshots;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Composition.Revisions;
using FluxFlow.Engine.Ports;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxFlow.Engine.Hosting;

public sealed class ApplicationRuntimeAssembler :
    IApplicationRevisionCandidateFactory,
    IApplicationRevisionEventSink,
    IApplicationRuntimeAccess,
    IAsyncDisposable
{
    private const int PendingRevisionEventCapacity = 256;
    private readonly CompositionNodeRegistry _registry;
    private readonly IReadOnlyList<IApplicationRuntimeServicesContributor> _serviceContributors;
    private readonly IServiceProvider _hostServices;
    private readonly ApplicationRuntimeAssemblerOptions _options;
    private readonly ILogger<ApplicationRuntimeAssembler>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _eventGate = new();
    private readonly Queue<ApplicationRevisionEvent> _pendingRevisionEvents = [];
    private IReadOnlyList<PortSurfaceEntry>? _surface;
    private ApplicationPortRuntime? _ports;
    private int _disposed;

    public ApplicationRuntimeAssembler(
        CompositionNodeRegistry registry,
        IEnumerable<IApplicationRuntimeServicesContributor> serviceContributors,
        IServiceProvider hostServices,
        IOptions<ApplicationRuntimeAssemblerOptions> options,
        ILogger<ApplicationRuntimeAssembler>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentNullException.ThrowIfNull(serviceContributors);
        _serviceContributors = serviceContributors.ToArray();
        _hostServices = hostServices ?? throw new ArgumentNullException(nameof(hostServices));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        if (_options.InputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Input capacity must be greater than zero.");
        if (_options.OutputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Output capacity must be greater than zero.");
    }

    public ApplicationPortRuntime? Ports => Volatile.Read(ref _ports);

    public ApplicationPortRuntime GetRequiredPorts()
        => Ports ?? throw new InvalidOperationException(
            "Application ports are unavailable until the first revision is active.");

    public async ValueTask<IApplicationRevisionCandidate> PrepareAsync(
        ApplicationRevisionPreparationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await PrepareCoreAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask<bool> PublishAsync(
        ApplicationRevisionEvent revisionEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionEvent);
        lock (_eventGate)
        {
            var ports = _ports;
            if (ports is not null)
                return ports.PublishAsync(revisionEvent, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (_pendingRevisionEvents.Count >= PendingRevisionEventCapacity)
                return ValueTask.FromResult(false);
            _pendingRevisionEvents.Enqueue(revisionEvent);
            return ValueTask.FromResult(true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var ports = Interlocked.Exchange(ref _ports, null);
            if (ports is not null)
                await ports.DisposeAsync().ConfigureAwait(false);
            _surface = null;
            lock (_eventGate)
                _pendingRevisionEvents.Clear();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async ValueTask<IApplicationRevisionCandidate> PrepareCoreAsync(
        ApplicationRevisionPreparationContext context,
        CancellationToken cancellationToken)
    {
        var definition = context.Plan.Next;
        var compilation = new ApplicationLinkCompiler(
                _registry,
                _hostServices.GetService<IFlowExpressionEngine>(),
                ApplicationPortRuntimeBuilder.SystemOutputs)
            .Compile(definition);
        if (!compilation.IsValid)
            throw new ApplicationRuntimeAssemblerException(compilation.Diagnostics);

        var surface = CreateSurface(definition);
        var existingPorts = Ports;
        if (existingPorts is not null)
            ValidateStableSurface(surface);

        var snapshots = new List<CompositionServiceProviderSnapshot>();
        var descriptors = new Dictionary<ComponentKey, BuiltComponent>();
        CompositionRuntime? runtime = null;
        ApplicationPortRuntime? preparedPorts = null;
        ApplicationPortRevision? portRevision = null;
        var ownsPorts = false;

        try
        {
            var candidateServices = new ServiceCollection();
            var servicesContext = new ApplicationRuntimeServicesContext(
                definition,
                context,
                _hostServices,
                candidateServices);
            foreach (var contributor in _serviceContributors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                contributor.Configure(servicesContext);
            }

            var resourceSnapshot = new CompositionServiceProviderSnapshotBuilder()
                .AddServices(candidateServices)
                .Build(
                    CompositionProviderBoundary.ResourceRevision,
                    $"resources:{context.RevisionId}");
            snapshots.Add(resourceSnapshot);

            foreach (var (workflowName, workflow) in definition.Workflows)
            {
                foreach (var (componentName, component) in workflow.Components)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var registration = _registry.Registrations[component.Type];
                    var descriptor = await CreateComponentAsync(
                            workflowName,
                            componentName,
                            component,
                            registration,
                            resourceSnapshot.Services)
                        .ConfigureAwait(false);
                    descriptors.Add(
                        new ComponentKey(workflowName, componentName),
                        new BuiltComponent(registration, descriptor));
                }
            }

            foreach (var (workflowName, workflow) in definition.Workflows)
            {
                var workflowServices = new ServiceCollection();
                foreach (var componentName in workflow.Components.Keys)
                {
                    var key = new ComponentKey(workflowName, componentName);
                    RegisterWorkflowViews(workflowServices, key, descriptors[key]);
                }

                snapshots.Add(new CompositionServiceProviderSnapshotBuilder()
                    .AddServices(workflowServices)
                    .Build(
                        CompositionProviderBoundary.WorkflowRevision,
                        $"workflow:{workflowName}:{context.RevisionId}"));
            }

            var composedNodes = descriptors.Values.Select(static value => value.Descriptor).ToArray();
            runtime = CompositionRuntime.Create(composedNodes, [], composedNodes);

            preparedPorts = existingPorts ?? CreatePortRuntime(surface);
            ownsPorts = existingPorts is null;
            await using (var revisionBuilder = preparedPorts.CreateRevision(context.RevisionId))
            {
                foreach (var (key, component) in descriptors)
                    ConfigureRevisionPorts(revisionBuilder, key, component);
                revisionBuilder.SetLinks(compilation.Links);
                portRevision = revisionBuilder.Build();
            }

            return new ApplicationRuntimeRevisionCandidate(
                runtime,
                portRevision,
                preparedPorts,
                snapshots,
                ownsPorts,
                ownsPorts ? () => AdoptPortsAsync(preparedPorts, surface) : null);
        }
        catch (Exception preparationFailure)
        {
            var cleanupFailures = new List<Exception>();
            if (portRevision is not null)
            {
                try
                {
                    await portRevision.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception);
                }
            }

            try
            {
                if (runtime is not null)
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await DisposeDescriptorsAsync(descriptors.Values.Select(static value => value.Descriptor))
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            try
            {
                await DisposeSnapshotsAsync(snapshots).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            if (ownsPorts && preparedPorts is not null)
            {
                try
                {
                    await preparedPorts.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception);
                }
            }

            if (cleanupFailures.Count == 0)
                throw;

            cleanupFailures.Insert(0, preparationFailure);
            throw new AggregateException(
                "Canonical application runtime preparation and cleanup failed.",
                cleanupFailures);
        }
    }

    private async ValueTask<ComposedNode> CreateComponentAsync(
        string workflowName,
        string componentName,
        ComponentDefinition definition,
        CompositionNodeRegistration registration,
        IServiceProvider services)
    {
        ComposedNode descriptor;
        try
        {
            descriptor = await registration.Factory(new CompositionNodeFactoryContext(
                    services,
                    workflowName,
                    componentName,
                    definition))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Factory for component '{workflowName}.{componentName}' failed: {exception.Message}",
                exception);
        }

        if (descriptor is null)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Factory for component '{workflowName}.{componentName}' returned null.");
        }

        try
        {
            ValidateDescriptor(workflowName, componentName, registration, descriptor);
            return descriptor;
        }
        catch (Exception validationFailure)
        {
            try
            {
                await descriptor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    $"Component '{workflowName}.{componentName}' validation and cleanup failed.",
                    validationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    private static void ValidateDescriptor(
        string workflowName,
        string componentName,
        CompositionNodeRegistration registration,
        ComposedNode descriptor)
    {
        if (descriptor.Inputs.Count != registration.Inputs.Count ||
            descriptor.Outputs.Count != registration.Outputs.Count)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Component '{workflowName}.{componentName}' descriptor ports do not exactly match its registration.");
        }

        foreach (var (name, metadata) in registration.Inputs)
        {
            if (!descriptor.Inputs.TryGetValue(name, out var input) ||
                input.Kind != metadata.Kind ||
                (metadata.Kind == CompositionPortKind.Message && input.MessageType != metadata.MessageType))
            {
                throw new ApplicationRuntimeAssemblerException(
                    $"Component '{workflowName}.{componentName}' input '{name}' does not match its registration.");
            }
        }

        foreach (var (name, metadata) in registration.Outputs)
        {
            if (!descriptor.Outputs.TryGetValue(name, out var output) ||
                output.MessageType != metadata.MessageType)
            {
                throw new ApplicationRuntimeAssemblerException(
                    $"Component '{workflowName}.{componentName}' output '{name}' does not match its registration.");
            }
        }
    }

    private IReadOnlyList<PortSurfaceEntry> CreateSurface(ApplicationDefinition definition)
    {
        var entries = new List<PortSurfaceEntry>();
        foreach (var (workflowName, workflow) in definition.Workflows)
        {
            foreach (var (componentName, component) in workflow.Components)
            {
                if (!_registry.TryGetRegistration(component.Type, out var registration))
                {
                    throw new ApplicationRuntimeAssemblerException(
                        $"Component '{workflowName}.{componentName}' uses unregistered type '{component.Type}'.");
                }

                foreach (var metadata in registration.Inputs.Values)
                    AddSurfaceEntry(entries, workflowName, componentName, metadata, ApplicationPortDirection.Input);
                foreach (var metadata in registration.Outputs.Values)
                    AddSurfaceEntry(entries, workflowName, componentName, metadata, ApplicationPortDirection.Output);
            }
        }

        return entries
            .OrderBy(static entry => entry.Address.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddSurfaceEntry(
        ICollection<PortSurfaceEntry> entries,
        string workflowName,
        string componentName,
        CompositionPortMetadata metadata,
        ApplicationPortDirection direction)
    {
        if (!metadata.SupportsTypeVisit)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Component '{workflowName}.{componentName}' port '{metadata.Name}' does not carry " +
                "reflection-free typed metadata.");
        }

        entries.Add(new PortSurfaceEntry(
            ApplicationAddress.WorkflowPort(workflowName, componentName, metadata.Name),
            direction,
            metadata));
    }

    private ApplicationPortRuntime CreatePortRuntime(IReadOnlyList<PortSurfaceEntry> surface)
    {
        var builder = new ApplicationPortRuntimeBuilder();
        if (_logger is not null)
            builder.UseLogger(_logger);

        foreach (var entry in surface)
        {
            entry.Metadata.Accept(new RuntimePortBuilderVisitor(
                builder,
                entry.Address,
                entry.Direction,
                _options));
        }

        return builder.Build();
    }

    private void ValidateStableSurface(IReadOnlyList<PortSurfaceEntry> next)
    {
        var current = _surface ?? throw new InvalidOperationException(
            "The stable port runtime exists without its registered surface.");
        if (current.Count == next.Count && current.Zip(next).All(static pair => pair.First.IsSame(pair.Second)))
            return;

        throw new ApplicationRuntimeAssemblerException(
            "The candidate changes the registered port address, direction, kind, or payload type. " +
            "Dynamic port-surface changes require a new application runtime.");
    }

    private async ValueTask AdoptPortsAsync(
        ApplicationPortRuntime ports,
        IReadOnlyList<PortSurfaceEntry> surface)
    {
        while (true)
        {
            ApplicationRevisionEvent[] pending;
            lock (_eventGate)
            {
                if (_pendingRevisionEvents.Count == 0)
                {
                    if (Interlocked.CompareExchange(ref _ports, ports, null) is not null)
                    {
                        throw new InvalidOperationException(
                            "The stable application port runtime is already initialized.");
                    }

                    _surface = surface.ToArray();
                    return;
                }

                pending = _pendingRevisionEvents.ToArray();
                _pendingRevisionEvents.Clear();
            }

            foreach (var revisionEvent in pending)
            {
                if (!await ports.PublishAsync(revisionEvent, CancellationToken.None).ConfigureAwait(false))
                {
                    throw new ApplicationRuntimeAssemblerException(
                        "The initial revision event stream completed before activation.");
                }
            }
        }
    }

    private static void ConfigureRevisionPorts(
        ApplicationPortRevisionBuilder builder,
        ComponentKey key,
        BuiltComponent component)
    {
        foreach (var metadata in component.Registration.Inputs.Values)
        {
            var address = ApplicationAddress.WorkflowPort(
                key.WorkflowName,
                key.ComponentName,
                metadata.Name);
            metadata.Accept(new RevisionInputVisitor(
                builder,
                address,
                component.Descriptor.Inputs[metadata.Name]));
        }

        foreach (var metadata in component.Registration.Outputs.Values)
        {
            var address = ApplicationAddress.WorkflowPort(
                key.WorkflowName,
                key.ComponentName,
                metadata.Name);
            metadata.Accept(new RevisionOutputVisitor(
                builder,
                address,
                component.Descriptor.Outputs[metadata.Name]));
        }
    }

    private static void RegisterWorkflowViews(
        IServiceCollection services,
        ComponentKey key,
        BuiltComponent component)
    {
        var componentAddress = ApplicationAddress.WorkflowComponent(
            key.WorkflowName,
            key.ComponentName);
        services.AddKeyedSingleton<IFlowNode>(
            componentAddress.Value,
            new NonOwningFlowNodeView(component.Descriptor.Node));
        services.AddKeyedSingleton(componentAddress.Value, component.Descriptor);

        foreach (var metadata in component.Registration.Inputs.Values)
        {
            var address = ApplicationAddress.WorkflowPort(
                key.WorkflowName,
                key.ComponentName,
                metadata.Name);
            metadata.Accept(new WorkflowInputViewVisitor(
                services,
                address,
                component.Descriptor.Inputs[metadata.Name]));
        }

        foreach (var metadata in component.Registration.Outputs.Values)
        {
            var address = ApplicationAddress.WorkflowPort(
                key.WorkflowName,
                key.ComponentName,
                metadata.Name);
            metadata.Accept(new WorkflowOutputViewVisitor(
                services,
                address,
                component.Descriptor.Outputs[metadata.Name]));
        }
    }

    private static async ValueTask DisposeDescriptorsAsync(IEnumerable<ComposedNode> descriptors)
    {
        List<Exception>? failures = null;
        foreach (var descriptor in descriptors.Reverse())
        {
            try
            {
                await descriptor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException("Component cleanup failed during runtime preparation.", failures);
    }

    private static async ValueTask DisposeSnapshotsAsync(
        IEnumerable<CompositionServiceProviderSnapshot> snapshots)
    {
        List<Exception>? failures = null;
        foreach (var snapshot in snapshots.Reverse())
        {
            try
            {
                await snapshot.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException("Provider snapshot cleanup failed during runtime preparation.", failures);
    }

    private sealed record BuiltComponent(
        CompositionNodeRegistration Registration,
        ComposedNode Descriptor);

    private sealed record PortSurfaceEntry(
        ApplicationAddress Address,
        ApplicationPortDirection Direction,
        CompositionPortMetadata Metadata)
    {
        public bool IsSame(PortSurfaceEntry other)
            => Address == other.Address &&
               Direction == other.Direction &&
               Metadata.Kind == other.Metadata.Kind &&
               Metadata.MessageType == other.Metadata.MessageType;
    }

    private readonly record struct ComponentKey(string WorkflowName, string ComponentName);

    private sealed class RuntimePortBuilderVisitor(
        ApplicationPortRuntimeBuilder builder,
        ApplicationAddress address,
        ApplicationPortDirection direction,
        ApplicationRuntimeAssemblerOptions options) : ICompositionPortTypeVisitor
    {
        public void Visit<TMessage>(CompositionPortMetadata metadata)
        {
            if (direction == ApplicationPortDirection.Input)
                builder.AddInput<TMessage>(address, options.InputCapacity);
            else
                builder.AddOutput<TMessage>(address, options.OutputCapacity);
        }

        public void VisitSignal(CompositionPortMetadata metadata)
        {
            if (direction != ApplicationPortDirection.Input)
                throw new ApplicationRuntimeAssemblerException($"Signal output '{address}' is unsupported.");
            builder.AddSignalInput(address, options.InputCapacity);
        }
    }

    private sealed class RevisionInputVisitor(
        ApplicationPortRevisionBuilder builder,
        ApplicationAddress address,
        CompositionInputPort input) : ICompositionPortTypeVisitor
    {
        public void Visit<TMessage>(CompositionPortMetadata metadata)
        {
            if (input is not CompositionInputPort<TMessage> typed)
                throw new ApplicationRuntimeAssemblerException($"Input descriptor '{address}' has the wrong type.");
            builder.ReplaceInput(address, typed.Target);
        }

        public void VisitSignal(CompositionPortMetadata metadata)
        {
            if (input is not CompositionSignalInputPort signal)
                throw new ApplicationRuntimeAssemblerException($"Signal descriptor '{address}' has the wrong kind.");
            builder.ReplaceSignalInput(address, signal.Target);
        }
    }

    private sealed class RevisionOutputVisitor(
        ApplicationPortRevisionBuilder builder,
        ApplicationAddress address,
        CompositionOutputPort output) : ICompositionPortTypeVisitor
    {
        public void Visit<TMessage>(CompositionPortMetadata metadata)
        {
            if (output is not CompositionOutputPort<TMessage> typed)
                throw new ApplicationRuntimeAssemblerException($"Output descriptor '{address}' has the wrong type.");
            builder.AttachOutput(address, typed.Source);
        }

        public void VisitSignal(CompositionPortMetadata metadata)
            => throw new ApplicationRuntimeAssemblerException($"Signal output '{address}' is unsupported.");
    }

    private sealed class WorkflowInputViewVisitor(
        IServiceCollection services,
        ApplicationAddress address,
        CompositionInputPort input) : ICompositionPortTypeVisitor
    {
        public void Visit<TMessage>(CompositionPortMetadata metadata)
        {
            if (input is not CompositionInputPort<TMessage> typed)
                throw new ApplicationRuntimeAssemblerException($"Input descriptor '{address}' has the wrong type.");
            services.AddExternalFluxFlowInputPort(address, typed.Target);
        }

        public void VisitSignal(CompositionPortMetadata metadata)
        {
            if (input is not CompositionSignalInputPort signal)
                throw new ApplicationRuntimeAssemblerException($"Signal descriptor '{address}' has the wrong kind.");
            services.AddExternalFluxFlowSignalTarget(address, signal.Target);
        }
    }

    private sealed class WorkflowOutputViewVisitor(
        IServiceCollection services,
        ApplicationAddress address,
        CompositionOutputPort output) : ICompositionPortTypeVisitor
    {
        public void Visit<TMessage>(CompositionPortMetadata metadata)
        {
            if (output is not CompositionOutputPort<TMessage> typed)
                throw new ApplicationRuntimeAssemblerException($"Output descriptor '{address}' has the wrong type.");
            services.AddExternalFluxFlowOutputPort(address, typed.Source);
        }

        public void VisitSignal(CompositionPortMetadata metadata)
            => throw new ApplicationRuntimeAssemblerException($"Signal output '{address}' is unsupported.");
    }

    private sealed class NonOwningFlowNodeView(IFlowNode node) : IFlowNode
    {
        public Task Completion => node.Completion;

        public void Complete() => node.Complete();

        public void Fault(Exception exception) => node.Fault(exception);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
