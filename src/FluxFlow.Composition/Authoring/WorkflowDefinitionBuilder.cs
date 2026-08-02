using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Authoring;

public sealed class WorkflowDefinitionBuilder
{
    private readonly Dictionary<string, ComponentEntry> _components = new(StringComparer.Ordinal);
    private readonly List<ApplicationLinkDeclarationProjection> _connections = [];
    private readonly AuthoringScope _owner;

    internal WorkflowDefinitionBuilder(AuthoringScope owner, string name)
    {
        _owner = owner;
        Name = name;
    }

    public string Name { get; }

    public ComponentHandle AddComponent(
        string name,
        string type,
        Action<ComponentDefinitionBuilder>? configure = null)
    {
        var committed = AddComponentCore(name, type, configure);
        return new ComponentHandle(
            _owner,
            this,
            ApplicationAddress.WorkflowComponent(Name, committed.Name),
            committed.Entry.Type);
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
        var committed = AddComponentCore(name, type, configure);
        return new ComponentHandle<TComponent>(
            _owner,
            this,
            ApplicationAddress.WorkflowComponent(Name, committed.Name),
            committed.Entry.Type);
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
        string? condition = null)
    {
        AddConnection(source, target, condition, allowCrossWorkflow: false);
        return this;
    }

    public WorkflowDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        string? condition = null)
    {
        AddConnection(source, target, condition, allowCrossWorkflow: false);
        return this;
    }

    internal WorkflowDefinition Build()
    {
        var linkProperties = _connections
            .GroupBy(static connection => connection.DeclaredPort)
            .ToDictionary(
                static group => group.Key,
                static group => ApplicationLinkCompiler.SerializeDeclarations(group),
                ApplicationAddressEqualityComparer.Instance);

        var definitions = new Dictionary<string, ComponentDefinition>(StringComparer.Ordinal);
        foreach (var (componentName, entry) in _components)
        {
            var properties = entry.Properties.ToDictionary(
                static property => property.Key,
                static property => property.Value.Clone(),
                StringComparer.Ordinal);

            foreach (var (port, links) in linkProperties.Where(pair =>
                         string.Equals(pair.Key.Segments[0], Name, StringComparison.Ordinal) &&
                         string.Equals(pair.Key.Segments[1], componentName, StringComparison.Ordinal)))
            {
                var portName = port.Segments[2];
                if (!properties.TryAdd(portName, links))
                {
                    throw new InvalidOperationException(
                        $"Component '{Name}.{componentName}' configures port '{portName}' both as a raw property and through Connect().");
                }
            }

            definitions.Add(componentName, new ComponentDefinition(entry.Type, properties));
        }

        return new WorkflowDefinition(definitions);
    }

    internal void AddConnection(
        PortHandle source,
        PortHandle target,
        string? condition,
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

        var connection = new ApplicationLinkDeclarationProjection(
            source.Address,
            target.Address,
            condition,
            ApplicationLinkDeclarationSide.Output);

        if (_connections.Any(existing =>
                existing.Source == connection.Source &&
                existing.Target == connection.Target &&
                string.Equals(existing.ConditionExpression, connection.ConditionExpression, StringComparison.Ordinal)))
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

    private (string Name, ComponentEntry Entry) AddComponentCore(
        string name,
        string type,
        Action<ComponentDefinitionBuilder>? configure)
    {
        _owner.EnsureMutable();
        name = DefinitionRules.RequireSegment(name, nameof(name), "Component name");
        type = DefinitionRules.RequireType(type, nameof(type));
        if (_components.ContainsKey(name))
            throw new ArgumentException($"Workflow '{Name}' contains duplicate component '{name}'.", nameof(name));

        var builder = new ComponentDefinitionBuilder(_owner);
        configure?.Invoke(builder);
        var entry = new ComponentEntry(type, builder.Commit());

        _ = new ComponentDefinition(entry.Type, entry.Properties);
        _components.Add(name, entry);
        return (name, entry);
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

    private sealed class ApplicationAddressEqualityComparer : IEqualityComparer<ApplicationAddress>
    {
        public static ApplicationAddressEqualityComparer Instance { get; } = new();

        public bool Equals(ApplicationAddress? x, ApplicationAddress? y) => x == y;

        public int GetHashCode(ApplicationAddress obj) => obj.GetHashCode();
    }
}
