using FluxFlow.Nodes;
using FluxFlow.Core;
using FluxFlow.Json.Model;

namespace FluxFlow.Composition.Definition;

public sealed class ApplicationDefinitionBuilder
{
    private string? _expressionEngine;

    public ApplicationDefinitionBuilder UseExpressions(string engine)
    {
        _owner.EnsureMutable();
        _expressionEngine = DefinitionRules.RequireType(engine, nameof(engine));
        return this;
    }

    private readonly DefinitionScope _owner = new();
    private readonly Dictionary<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>> _componentFactories = new(StringComparer.Ordinal);
    private readonly ApplicationResourceContractCollection _resourceContracts = new();
    private readonly ResourceContainerState _resources;
    private readonly Dictionary<string, WorkflowDefinitionBuilder> _workflows =
        new(StringComparer.Ordinal);

    public ApplicationDefinitionBuilder()
    {
        _resources = new ResourceContainerState(_owner, _resourceContracts);
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

        var workflow = new WorkflowDefinitionBuilder(_owner, _resourceContracts, name, _componentFactories);
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
        InputPortHandle<TMessage> target,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, allowCrossWorkflow: true, delivery: delivery);
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
        string condition,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, condition, allowCrossWorkflow: true, delivery: delivery);
        return this;
    }

    public ApplicationDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        InputPortHandle<TMessage> target,
        Func<TMessage, bool> when,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, when, allowCrossWorkflow: true, delivery: delivery);
        return this;
    }

    public ApplicationDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, allowCrossWorkflow: true, delivery: delivery);
        return this;
    }

    public ApplicationDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        string condition,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, condition, allowCrossWorkflow: true, delivery: delivery);
        return this;
    }

    public ApplicationDefinitionBuilder Connect<TMessage>(
        OutputPortHandle<TMessage> source,
        SignalInputPortHandle target,
        Func<TMessage, bool> when,
        FlowDeliveryPolicy delivery = FlowDeliveryPolicy.Reliable)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSameOwner(source);
        EnsureSameOwner(target);
        source.Workflow.AddConnection(source, target, when, allowCrossWorkflow: true, delivery: delivery);
        return this;
    }

    public ApplicationSetup Build()
    {
        _owner.EnsureMutable();

        var definition = new ApplicationSetup(new ApplicationDefinition(
            _resources.Build(),
            _workflows.Select(static workflow =>
                new KeyValuePair<string, WorkflowDefinition>(
                    workflow.Key,
                    workflow.Value.Build())), _expressionEngine),
            _workflows.Values.SelectMany(static workflow => workflow.BuildLinks()),
            _resourceContracts.Build(), _componentFactories);

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

