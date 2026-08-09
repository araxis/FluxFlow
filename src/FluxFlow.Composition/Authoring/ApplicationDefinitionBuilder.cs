using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Authoring;

public sealed class ApplicationDefinitionBuilder : IResourceDefinitionContainerBuilder
{
    private readonly AuthoringScope _owner = new();
    private readonly ComponentContractCollection _componentContracts = new();
    private readonly ApplicationResourceContractCollection _resourceContracts = new();
    private readonly ResourceContainerState _resources;
    private readonly Dictionary<string, WorkflowDefinitionBuilder> _workflows =
        new(StringComparer.Ordinal);

    public ApplicationDefinitionBuilder()
    {
        _resources = new ResourceContainerState(_owner, _resourceContracts, []);
    }

    public ResourceGroupBuilder AddResourceGroup(string name)
        => _resources.AddResourceGroup(name);

    public ApplicationDefinitionBuilder AddResourceGroup(
        string name,
        out ResourceGroupBuilder group)
    {
        group = AddResourceGroup(name);
        return this;
    }

    public ResourceHandle AddResource(
        string name,
        string type,
        Action<ResourceDefinitionBuilder>? configure = null)
        => _resources.AddResource(name, type, configure);

    public ApplicationDefinitionBuilder AddResource(
        string name,
        string type,
        out ResourceHandle resource)
    {
        resource = AddResource(name, type);
        return this;
    }

    public ApplicationDefinitionBuilder AddResource(
        string name,
        string type,
        Action<ResourceDefinitionBuilder> configure,
        out ResourceHandle resource)
    {
        ArgumentNullException.ThrowIfNull(configure);
        resource = AddResource(name, type, configure);
        return this;
    }

    public ResourceHandle<TResource> AddResource<TResource>(
        string name,
        string type,
        Action<ResourceDefinitionBuilder>? configure = null)
        => _resources.AddResource<TResource>(name, type, configure);

    public ApplicationDefinitionBuilder AddResource<TResource>(
        string name,
        string type,
        out ResourceHandle<TResource> resource)
    {
        resource = AddResource<TResource>(name, type);
        return this;
    }

    public ApplicationDefinitionBuilder AddResource<TResource>(
        string name,
        string type,
        Action<ResourceDefinitionBuilder> configure,
        out ResourceHandle<TResource> resource)
    {
        ArgumentNullException.ThrowIfNull(configure);
        resource = AddResource<TResource>(name, type, configure);
        return this;
    }

    public WorkflowDefinitionBuilder AddWorkflow(string name)
    {
        _owner.EnsureMutable();
        name = DefinitionRules.RequireWorkflowName(name, nameof(name));
        if (_workflows.ContainsKey(name))
            throw new ArgumentException($"Application contains duplicate workflow '{name}'.", nameof(name));

        var workflow = new WorkflowDefinitionBuilder(_owner, _componentContracts, name);
        _workflows.Add(name, workflow);
        return workflow;
    }

    public ApplicationDefinitionBuilder AddWorkflow(
        string name,
        out WorkflowDefinitionBuilder workflow)
    {
        workflow = AddWorkflow(name);
        return this;
    }

    public ApplicationDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        InputPortHandle<TMessage> target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, allowCrossWorkflow: true);
        return this;
    }

    public THandle AddResource<THandle>(
        string name,
        ApplicationResourceContract<THandle> resource)
        where THandle : AuthoredResourceHandle
        => _resources.AddResource(name, resource);

    public ApplicationDefinitionBuilder AddResource<THandle>(
        string name,
        ApplicationResourceContract<THandle> resource,
        out THandle handle)
        where THandle : AuthoredResourceHandle
    {
        handle = AddResource(name, resource);
        return this;
    }

    public THandle AddResource<TOptions, THandle>(
        string name,
        ApplicationResourceContract<TOptions, THandle> resource,
        Action<TOptions> configure)
        where TOptions : class
        where THandle : AuthoredResourceHandle
        => _resources.AddResource(name, resource, configure);

    public ApplicationDefinitionBuilder AddResource<TOptions, THandle>(
        string name,
        ApplicationResourceContract<TOptions, THandle> resource,
        Action<TOptions> configure,
        out THandle handle)
        where TOptions : class
        where THandle : AuthoredResourceHandle
    {
        handle = AddResource(name, resource, configure);
        return this;
    }

    public ApplicationDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        InputPortHandle<TMessage> target,
        string condition)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, condition, allowCrossWorkflow: true);
        return this;
    }

    public ApplicationDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        InputPortHandle<TMessage> target,
        Func<TMessage, bool> when)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, when, allowCrossWorkflow: true);
        return this;
    }

    public ApplicationDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, allowCrossWorkflow: true);
        return this;
    }

    public ApplicationDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        string condition)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, condition, allowCrossWorkflow: true);
        return this;
    }

    public ApplicationDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        Func<TMessage, bool> when)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, when, allowCrossWorkflow: true);
        return this;
    }

    public ApplicationDefinition Build()
    {
        _owner.EnsureMutable();

        var definition = new ApplicationDefinition(
            _resources.Build(),
            _workflows.Select(static workflow =>
                new KeyValuePair<string, WorkflowDefinition>(
                    workflow.Key,
                    workflow.Value.Build())),
            _workflows.Values.SelectMany(static workflow => workflow.BuildLinks()),
            _componentContracts.Build(),
            _resourceContracts.Build());

        _owner.Complete();
        return definition;
    }

    private void EnsureSameOwner(PortHandle port)
    {
        if (!ReferenceEquals(port.Owner, _owner))
        {
            throw new InvalidOperationException(
                $"Port '{port.Address}' belongs to a different application definition builder.");
        }
    }
}
