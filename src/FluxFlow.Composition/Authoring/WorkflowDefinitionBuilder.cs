using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Authoring;

public sealed class WorkflowDefinitionBuilder
{
    private readonly Dictionary<string, ComponentEntry> _components = new(StringComparer.Ordinal);
    private readonly List<ApplicationLinkDefinition> _connections = [];
    private readonly ComponentContractCollection _componentContracts;
    private readonly AuthoringScope _owner;

    internal WorkflowDefinitionBuilder(
        AuthoringScope owner,
        ComponentContractCollection componentContracts,
        string name)
    {
        _owner = owner;
        _componentContracts = componentContracts;
        Name = name;
    }

    public string Name { get; }

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

    public THandle AddComponent<THandle>(
        string name,
        ComponentContract<THandle> component)
        where THandle : AuthoredComponentHandle
    {
        ArgumentNullException.ThrowIfNull(component);
        return AddComponentCore(
            name,
            component.Type,
            configure: null,
            component.CreateHandle,
            component.Descriptor);
    }

    public WorkflowDefinitionBuilder AddComponent<THandle>(
        string name,
        ComponentContract<THandle> component,
        out THandle handle)
        where THandle : AuthoredComponentHandle
    {
        handle = AddComponent(name, component);
        return this;
    }

    public THandle AddComponent<TOptions, THandle>(
        string name,
        ComponentContract<TOptions, THandle> component,
        Action<TOptions> configure)
        where TOptions : class
        where THandle : AuthoredComponentHandle
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(configure);
        return AddConfiguredComponentCore(
            name,
            component,
            configure);
    }

    public WorkflowDefinitionBuilder AddComponent<TOptions, THandle>(
        string name,
        ComponentContract<TOptions, THandle> component,
        Action<TOptions> configure,
        out THandle handle)
        where TOptions : class
        where THandle : AuthoredComponentHandle
    {
        handle = AddComponent(name, component, configure);
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
        InputPortHandle<TMessage> target)
    {
        AddConnection(source, target, allowCrossWorkflow: false);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        InputPortHandle<TMessage> target,
        string condition)
    {
        AddConnection(source, target, condition, allowCrossWorkflow: false);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        InputPortHandle<TMessage> target,
        Func<TMessage, bool> when)
    {
        AddConnection(source, target, when, allowCrossWorkflow: false);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target)
    {
        AddConnection(source, target, allowCrossWorkflow: false);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        string condition)
    {
        AddConnection(source, target, condition, allowCrossWorkflow: false);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        Func<TMessage, bool> when)
    {
        AddConnection(source, target, when, allowCrossWorkflow: false);
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

            definitions.Add(componentName, new ComponentDefinition(entry.Type, properties));
        }

        return new WorkflowDefinition(definitions);
    }

    internal IReadOnlyList<ApplicationLinkDefinition> BuildLinks() => _connections.ToArray();

    internal void AddConnection<TMessage>(
        OutputPortHandle<TMessage> source,
        PortHandle target,
        bool allowCrossWorkflow)
        => AddConnectionCore(
            source,
            target,
            ApplicationLinkDefinition.Unconditional<TMessage>(
                source.Address,
                target.Address,
                ApplicationLinkDeclarationSide.Output),
            allowCrossWorkflow);

    internal void AddConnection<TMessage>(
        OutputPortHandle<TMessage> source,
        PortHandle target,
        string condition,
        bool allowCrossWorkflow)
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
                ApplicationLinkDeclarationSide.Output),
            allowCrossWorkflow);
    }

    internal void AddConnection<TMessage>(
        OutputPortHandle<TMessage> source,
        PortHandle target,
        Func<TMessage, bool> when,
        bool allowCrossWorkflow)
    {
        ArgumentNullException.ThrowIfNull(when);
        AddConnectionCore(
            source,
            target,
            ApplicationLinkDefinition.Predicate(
                source.Address,
                target.Address,
                when,
                ApplicationLinkDeclarationSide.Output),
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

        if (_connections.Any(existing =>
                existing.Equals(connection)))
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

    private THandle AddComponentCore<THandle>(
        string name,
        string type,
        Action<ComponentDefinitionBuilder>? configure,
        Func<ComponentHandle, THandle> createHandle,
        ComponentDescriptor? descriptor = null)
        where THandle : class
    {
        var component = PrepareComponent(name, type);
        if (descriptor is not null)
            _componentContracts.EnsureCanAdd(descriptor);
        return CommitComponent(
            component.Name,
            component.Type,
            configure,
            createHandle,
            descriptor);
    }

    private THandle AddConfiguredComponentCore<TOptions, THandle>(
        string name,
        ComponentContract<TOptions, THandle> component,
        Action<TOptions> configure)
        where TOptions : class
        where THandle : AuthoredComponentHandle
    {
        var prepared = PrepareComponent(name, component.Type);
        _componentContracts.EnsureCanAdd(component.Descriptor);
        var options = component.CreateOptions();
        configure(options);
        return CommitComponent(
            prepared.Name,
            prepared.Type,
            definition => component.Apply(options, definition),
            component.CreateHandle,
            component.Descriptor);
    }

    private (string Name, string Type) PrepareComponent(string name, string type)
    {
        _owner.EnsureMutable();
        name = DefinitionRules.RequireSegment(name, nameof(name), "Component name");
        type = DefinitionRules.RequireType(type, nameof(type));
        if (_components.ContainsKey(name))
            throw new ArgumentException($"Workflow '{Name}' contains duplicate component '{name}'.", nameof(name));
        return (name, type);
    }

    private THandle CommitComponent<THandle>(
        string name,
        string type,
        Action<ComponentDefinitionBuilder>? configure,
        Func<ComponentHandle, THandle> createHandle,
        ComponentDescriptor? descriptor = null)
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
            $"Component '{Name}.{name}' returned no authoring handle.");
        if (descriptor is not null)
            _componentContracts.Add(descriptor);
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
