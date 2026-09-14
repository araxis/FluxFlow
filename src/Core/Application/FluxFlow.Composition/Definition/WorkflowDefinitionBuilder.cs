using FluxFlow.Nodes;
using FluxFlow.Core;
using FluxFlow.Json.Links;
using System.Text.Json;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;

namespace FluxFlow.Composition.Definition;

public sealed class WorkflowDefinitionBuilder
{
    private string? _expressionEngine;
    private readonly ResourceContainerState _resources;

    public WorkflowDefinitionBuilder UseExpressions(string engine)
    {
        _owner.EnsureMutable();
        _expressionEngine = DefinitionRules.RequireType(engine, nameof(engine));
        return this;
    }
    private readonly Dictionary<string, ComponentEntry> _components = new(StringComparer.Ordinal);
    private readonly List<ApplicationLinkDefinition> _connections = [];
    private readonly Dictionary<(string Component, string Policy), HashSet<string>> _outputPolicies = [];
    private readonly DefinitionScope _owner;
    private readonly Dictionary<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>> _componentFactories;

    internal WorkflowDefinitionBuilder(
        DefinitionScope owner,
        ApplicationResourceContractCollection resourceContracts,
        string name,
        Dictionary<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>> componentFactories)
    {
        _owner = owner;
        _componentFactories = componentFactories;
        Name = name;
        _resources = new ResourceContainerState(owner, resourceContracts, name);
    }

    public string Name { get; }

    public ResourceHandle AddResource(string name, string type, Action<ResourceDefinitionBuilder>? configure = null)
        => _resources.AddResource(name, type, configure);

    public ResourceHandle<TResource> AddResource<TResource>(string name, string type, Action<ResourceDefinitionBuilder>? configure = null)
        => _resources.AddResource<TResource>(name, type, configure);

    public THandle AddResource<THandle>(string name, ApplicationResourceContract<THandle> resource)
        where THandle : AuthoredResourceHandle => _resources.AddResource(name, resource);

    public THandle AddResource<TOptions, THandle>(string name, ApplicationResourceContract<TOptions, THandle> resource, Action<TOptions> configure)
        where TOptions : class where THandle : AuthoredResourceHandle => _resources.AddResource(name, resource, configure);

    public ComponentHandle AddComponent(
        string name,
        string type,
        Action<ComponentDefinitionBuilder>? configure = null)
    {
        return AddComponentCore(
            name,
            type,
            configure,
            static component => component);
    }

    public THandle AddComponent<THandle>(string name, string type,
        Func<ComponentActivationContext, ValueTask<IFlowNode>> factory,
        Action<ComponentDefinitionBuilder> configure,
        Func<ComponentHandle, THandle> createHandle) where THandle : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(createHandle);
        var prepared = PrepareComponent(name, type);
        if (_componentFactories.TryGetValue(prepared.Type, out var existing) && existing != factory)
            throw new InvalidOperationException($"Component type '{prepared.Type}' has conflicting factories.");
        var handle = CommitComponent(prepared.Name, prepared.Type, configure, createHandle);
        _componentFactories[prepared.Type] = factory;
        return handle;
    }

    public WorkflowDefinitionBuilder AddComponent(
        string name,
        string type,
        out ComponentHandle component)
    {
        component = AddComponent(name, type);
        return this;
    }

    public WorkflowDefinitionBuilder AddComponent(
        string name,
        string type,
        Action<ComponentDefinitionBuilder> configure,
        out ComponentHandle component)
    {
        ArgumentNullException.ThrowIfNull(configure);
        component = AddComponent(name, type, configure);
        return this;
    }

    public ComponentHandle<TComponent> AddComponent<TComponent>(
        string name,
        string type,
        Action<ComponentDefinitionBuilder>? configure = null)
    {
        return AddComponentCore(
            name,
            type,
            configure,
            component => new ComponentHandle<TComponent>(
                _owner,
                this,
                component.Address,
                component.Type));
    }

    public WorkflowDefinitionBuilder AddComponent<TComponent>(
        string name,
        string type,
        out ComponentHandle<TComponent> component)
    {
        component = AddComponent<TComponent>(name, type);
        return this;
    }

    public WorkflowDefinitionBuilder AddComponent<TComponent>(
        string name,
        string type,
        Action<ComponentDefinitionBuilder> configure,
        out ComponentHandle<TComponent> component)
    {
        ArgumentNullException.ThrowIfNull(configure);
        component = AddComponent<TComponent>(name, type, configure);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        InputPortHandle<TMessage> target,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        AddConnection(source, target, allowCrossWorkflow: false, delivery: delivery);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        InputPortHandle<TMessage> target,
        string condition,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        AddConnection(source, target, condition, allowCrossWorkflow: false, delivery: delivery);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        InputPortHandle<TMessage> target,
        Func<TMessage, bool> when,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        AddConnection(source, target, when, allowCrossWorkflow: false, delivery: delivery);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        AddConnection(source, target, allowCrossWorkflow: false, delivery: delivery);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        string condition,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        AddConnection(source, target, condition, allowCrossWorkflow: false, delivery: delivery);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        Func<TMessage, bool> when,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        AddConnection(source, target, when, allowCrossWorkflow: false, delivery: delivery);
        return this;
    }

    internal WorkflowDefinition Build()
    {
        var definitions = new Dictionary<string, ComponentDefinition>(StringComparer.Ordinal);
        foreach (var (componentName, entry) in _components)
        {
            var properties = entry.Properties.ToDictionary(
                static property => property.Key,
                static property => property.Value.Clone(),
                StringComparer.Ordinal);

            foreach (var (key, ports) in _outputPolicies.Where(pair => pair.Key.Component == componentName))
            {
                var existing = ApplicationOutputPolicies.Read(properties, key.Policy, $"{Name}.{componentName}", static _ => true);
                properties[key.Policy] = JsonSerializer.SerializeToElement(existing.Concat(ports)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            }
            definitions.Add(componentName, new ComponentDefinition(entry.Type, properties));
        }

        return new WorkflowDefinition(definitions, _expressionEngine, _resources.Build());
    }

    internal IReadOnlyList<ApplicationLinkDefinition> BuildLinks() => _connections.ToArray();

    internal void SetOutputPolicy<T>(OutputPortHandle<T> output, string policy)
    {
        _owner.EnsureMutable();
        ArgumentNullException.ThrowIfNull(output);
        EnsureSameOwner(output);
        if (!ReferenceEquals(output.Workflow, this))
            throw new InvalidOperationException($"Output '{output.Address}' does not belong to workflow '{Name}'.");
        var key = (output.Address.Segments[1], policy);
        if (!_outputPolicies.TryGetValue(key, out var ports)) _outputPolicies.Add(key, ports = new(StringComparer.Ordinal));
        ports.Add(output.Name);
    }

    internal void AddFallback<TMessage>(OutputPortHandle<TMessage> source, PortHandle target,
        ApplicationLinkDeclarationSide side, FlowDeliveryPolicy delivery)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        AddConnectionCore(source, target,
            ApplicationLinkDefinition.Fallback<TMessage>(source.Address, target.Address, side, delivery),
            allowCrossWorkflow: true);
    }

    internal void AddConnection<TMessage>(
        OutputPortHandle<TMessage> source,
        PortHandle target,
        bool allowCrossWorkflow,
        ApplicationLinkDeclarationSide declarationSide = ApplicationLinkDeclarationSide.Output,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
        => AddConnectionCore(
            source,
            target,
            ApplicationLinkDefinition.Unconditional<TMessage>(
                source.Address,
                target.Address,
                declarationSide, delivery),
            allowCrossWorkflow);

    internal void AddConnection<TMessage>(
        OutputPortHandle<TMessage> source,
        PortHandle target,
        string condition,
        bool allowCrossWorkflow,
        ApplicationLinkDeclarationSide declarationSide = ApplicationLinkDeclarationSide.Output,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        if (string.IsNullOrWhiteSpace(condition))
            throw new ArgumentException("Link condition cannot be empty or whitespace.", nameof(condition));
        AddConnectionCore(
            source,
            target,
            ApplicationLinkDefinition.Expression<TMessage>(
                source.Address,
                target.Address,
                condition,
                declarationSide, delivery),
            allowCrossWorkflow);
    }

    internal void AddConnection<TMessage>(
        OutputPortHandle<TMessage> source,
        PortHandle target,
        Func<TMessage, bool> when,
        bool allowCrossWorkflow,
        ApplicationLinkDeclarationSide declarationSide = ApplicationLinkDeclarationSide.Output,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(when);
        AddConnectionCore(
            source,
            target,
            ApplicationLinkDefinition.Predicate(
                source.Address,
                target.Address,
                when,
                declarationSide, delivery),
            allowCrossWorkflow);
    }

    private void AddConnectionCore(
        PortHandle source,
        PortHandle target,
        ApplicationLinkDefinition connection,
        bool allowCrossWorkflow)
    {
        _owner.EnsureMutable();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);

        if (!ReferenceEquals(source.Workflow, this))
        {
            throw new InvalidOperationException(
                $"Source port '{source.Address}' does not belong to workflow '{Name}'.");
        }

        if (!allowCrossWorkflow && !ReferenceEquals(target.Workflow, this))
        {
            throw new InvalidOperationException(
                $"Target port '{target.Address}' belongs to workflow '{target.Workflow.Name}'. " +
                "Use ApplicationDefinitionBuilder.Connect for cross-workflow connections.");
        }

        if (connection.IsFallback && _connections.Any(existing => existing.Source == connection.Source && existing.IsFallback))
            throw new InvalidOperationException($"Output '{source.Address}' already has a fallback.");

        if (_connections.Any(existing =>
                existing.Equals(connection) ||
                (existing.Source == connection.Source && existing.Target == connection.Target &&
                 existing.Delivery != connection.Delivery)))
        {
            throw new InvalidOperationException(
                $"Connection '{source.Address}' to '{target.Address}' is already declared.");
        }

        if (source.LinkCardinality == ComponentPortLinkCardinality.Single &&
            _connections.Any(existing => existing.Source == source.Address))
        {
            throw new InvalidOperationException(
                $"Output port '{source.Address}' accepts only one connection.");
        }

        if (target.LinkCardinality == ComponentPortLinkCardinality.Single &&
            _connections.Any(existing => existing.Target == target.Address))
        {
            throw new InvalidOperationException(
                $"Input port '{target.Address}' accepts only one connection.");
        }

        _connections.Add(connection);
    }

    private THandle AddComponentCore<THandle>(string name, string type,
        Action<ComponentDefinitionBuilder>? configure, Func<ComponentHandle, THandle> createHandle)
        where THandle : class
    {
        var component = PrepareComponent(name, type);
        return CommitComponent(component.Name, component.Type, configure, createHandle);
    }

    private (string Name, string Type) PrepareComponent(string name, string type)
    {
        _owner.EnsureMutable();
        name = DefinitionRules.RequireSegment(name, nameof(name), "Component name");
        if (name == CanonicalApplicationProperties.ExpressionEngine)
            throw new ArgumentException("Component name 'ExpressionEngine' is reserved for workflow configuration.", nameof(name));
        type = DefinitionRules.RequireType(type, nameof(type));
        if (_components.ContainsKey(name))
            throw new ArgumentException($"Workflow '{Name}' contains duplicate component '{name}'.", nameof(name));
        return (name, type);
    }

    private THandle CommitComponent<THandle>(
        string name,
        string type,
        Action<ComponentDefinitionBuilder>? configure,
        Func<ComponentHandle, THandle> createHandle)
        where THandle : class
    {
        var builder = new ComponentDefinitionBuilder(_owner);
        configure?.Invoke(builder);
        var entry = new ComponentEntry(type, builder.Commit());

        _ = new ComponentDefinition(entry.Type, entry.Properties);
        var component = new ComponentHandle(
            _owner,
            this,
            ApplicationAddress.WorkflowComponent(Name, name),
            entry.Type);
        var handle = createHandle(component) ?? throw new InvalidOperationException(
            $"Component '{Name}.{name}' returned no definition handle.");
        _components.Add(name, entry);
        return handle;
    }

    private void EnsureSameOwner(PortHandle port)
    {
        if (!ReferenceEquals(port.Owner, _owner))
        {
            throw new InvalidOperationException(
                $"Port '{port.Address}' belongs to a different application definition builder.");
        }
    }

    private sealed record ComponentEntry(
        string Type,
        IReadOnlyDictionary<string, JsonElement> Properties);

}
