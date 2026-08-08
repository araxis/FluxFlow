using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public class RuntimeComponentRegistrationBuilder
{
    private readonly List<ComponentPortMetadata> _inputs = [];
    private readonly List<ComponentPortMetadata> _outputs = [];
    private readonly List<ComponentOptionMetadata> _options = [];
    private readonly List<ComponentResourceMetadata> _resources = [];
    private readonly List<ComponentBinding> _bindings = [];
    private readonly List<ComponentBindingIdentity> _bindingIdentities = [];
    private Func<IReadOnlyList<ComponentBinding>, ComponentFactory>? _createFactory;
    private Delegate? _registrationFactory;
    private ComponentFactoryMode _factoryMode;

    protected internal RuntimeComponentRegistrationBuilder(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Type = type.Trim();
    }

    protected string Type { get; }

    protected CompositionProcessingCapabilities ProcessingCapabilities { get; private set; } =
        CompositionProcessingCapabilities.Sequential;

    public RuntimeComponentBindingBuilder<TNode> UseFactory<TNode>(
        Func<ComponentActivationContext, TNode> value)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(value);
        return UseTypedFactory(
            value,
            context => ValueTask.FromResult(CreateNodeActivation(value(context))));
    }

    public RuntimeComponentBindingBuilder<TNode> UseFactory<TNode>(
        Func<ComponentActivationContext, ValueTask<TNode>> value)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(value);
        return UseTypedFactory(value, async context =>
            CreateNodeActivation(await value(context).ConfigureAwait(false)));
    }

    public RuntimeComponentBindingBuilder<TNode> UseFactory<TNode>(
        Func<ComponentActivationContext, ComponentNodeActivation<TNode>> value)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(value);
        return UseTypedFactory(value, context => ValueTask.FromResult(value(context)));
    }

    public RuntimeComponentBindingBuilder<TNode> UseFactory<TNode>(
        Func<ComponentActivationContext, ValueTask<ComponentNodeActivation<TNode>>> value)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(value);
        return UseTypedFactory(value, value);
    }

    public RuntimeComponentInstanceBindingBuilder UseInstanceFactory(ComponentFactory value)
    {
        ArgumentNullException.ThrowIfNull(value);
        SetFactory(value, ComponentFactoryMode.Instance, _ => value);
        return new RuntimeComponentInstanceBindingBuilder(this);
    }

    public void UseProcessing(CompositionProcessingCapabilities capabilities)
    {
        if (!Enum.IsDefined(capabilities))
            throw new ArgumentOutOfRangeException(nameof(capabilities));

        ProcessingCapabilities = capabilities;
        OnProcessingChanged(capabilities);
    }

    public void AddOption<TValue>(string name, bool isRequired = false)
        => AddOption(ComponentOptions.Metadata<TValue>(name, isRequired));

    public void AddResource<TService>(
        string name,
        bool isRequired = false,
        string? valueTypeHint = null)
        => AddResource(ComponentResources.Metadata<TService>(name, isRequired, valueTypeHint));

    protected RuntimeComponentBindingBuilder<TNode> UseTypedFactory<TNode>(
        Delegate registrationFactory,
        Func<ComponentActivationContext, ValueTask<ComponentNodeActivation<TNode>>> activate)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(registrationFactory);
        ArgumentNullException.ThrowIfNull(activate);
        SetFactory(
            registrationFactory,
            ComponentFactoryMode.Node,
            bindings => context => ActivateAsync(Type, context, activate, bindings));
        return new RuntimeComponentBindingBuilder<TNode>(this);
    }

    protected RuntimeComponentInstanceBindingBuilder UseAdvancedFactory(ComponentFactory value)
        => UseInstanceFactory(value);

    protected internal ComponentDescriptor CreateDescriptor()
    {
        if (_createFactory is null || _registrationFactory is null)
        {
            throw new InvalidOperationException(
                $"Component type '{Type}' requires a factory. Call {nameof(UseFactory)} or {nameof(UseInstanceFactory)} during registration.");
        }

        var bindings = _bindings.ToArray();
        return new ComponentDescriptor(
            Type,
            _createFactory(bindings),
            _registrationFactory,
            _factoryMode,
            _bindingIdentities.ToArray(),
            _inputs,
            _outputs,
            ProcessingCapabilities,
            _options,
            _resources);
    }

    protected internal void CopyRuntimeConfigurationTo(RuntimeComponentRegistrationBuilder target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target._createFactory = _createFactory;
        target._registrationFactory = _registrationFactory;
        target._factoryMode = _factoryMode;
        target.ProcessingCapabilities = ProcessingCapabilities;
        target._inputs.AddRange(_inputs);
        target._outputs.AddRange(_outputs);
        target._options.AddRange(_options);
        target._resources.AddRange(_resources);
        target._bindings.AddRange(_bindings);
        target._bindingIdentities.AddRange(_bindingIdentities);
    }

    protected virtual void OnProcessingChanged(CompositionProcessingCapabilities capabilities)
    {
    }

    protected virtual void OnInputAdded(ComponentPortMetadata port)
    {
    }

    protected virtual void OnOutputAdded(ComponentPortMetadata port)
    {
    }

    protected virtual void OnOptionAdded(ComponentOptionMetadata option)
    {
    }

    protected virtual void OnResourceAdded(ComponentResourceMetadata resource)
    {
    }

    internal void AddInput<TNode, TMessage>(
        string name,
        Func<TNode, ITargetBlock<FlowMessage<TMessage>>> selector,
        ComponentPortLinkCardinality linkCardinality)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var metadata = ComponentPortMetadata.Create<TMessage>(name, linkCardinality);
        AddInput(metadata);
        var identity = new ComponentBindingIdentity(ComponentBindingRole.Input, metadata, selector);
        _bindingIdentities.Add(identity);
        _bindings.Add(new ComponentInputBinding<TNode, TMessage>(identity, selector));
    }

    internal void AddSignalInput<TNode>(
        string name,
        Func<TNode, IFlowSignalTarget> selector,
        ComponentPortLinkCardinality linkCardinality)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var metadata = ComponentPortMetadata.CreateSignal(name, linkCardinality);
        AddInput(metadata);
        var identity = new ComponentBindingIdentity(ComponentBindingRole.SignalInput, metadata, selector);
        _bindingIdentities.Add(identity);
        _bindings.Add(new ComponentSignalInputBinding<TNode>(identity, selector));
    }

    internal void AddOutput<TNode, TMessage>(
        string name,
        Func<TNode, ISourceBlock<FlowMessage<TMessage>>> selector,
        ComponentPortLinkCardinality linkCardinality)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var metadata = ComponentPortMetadata.Create<TMessage>(name, linkCardinality);
        AddOutput(metadata);
        var identity = new ComponentBindingIdentity(ComponentBindingRole.Output, metadata, selector);
        _bindingIdentities.Add(identity);
        _bindings.Add(new ComponentOutputBinding<TNode, TMessage>(identity, selector));
    }

    internal void AddEvents<TNode>(
        string name,
        Func<TNode, ISourceBlock<FlowEvent>> selector,
        ComponentPortLinkCardinality linkCardinality)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var metadata = ComponentPortMetadata.Create<ComponentEvent>(name, linkCardinality);
        AddOutput(metadata);
        var identity = new ComponentBindingIdentity(ComponentBindingRole.Events, metadata, selector);
        _bindingIdentities.Add(identity);
        _bindings.Add(new ComponentEventsBinding<TNode>(identity, selector));
    }

    internal void AddInstanceInput<TMessage>(string name, ComponentPortLinkCardinality linkCardinality)
        => AddInstancePort(ComponentBindingRole.Input, ComponentPortMetadata.Create<TMessage>(name, linkCardinality), input: true);

    internal void AddInstanceSignalInput(string name, ComponentPortLinkCardinality linkCardinality)
        => AddInstancePort(ComponentBindingRole.SignalInput, ComponentPortMetadata.CreateSignal(name, linkCardinality), input: true);

    internal void AddInstanceOutput<TMessage>(string name, ComponentPortLinkCardinality linkCardinality)
        => AddInstancePort(ComponentBindingRole.Output, ComponentPortMetadata.Create<TMessage>(name, linkCardinality), input: false);

    internal void AddInstanceEvents(string name, ComponentPortLinkCardinality linkCardinality)
        => AddInstancePort(ComponentBindingRole.Events, ComponentPortMetadata.Create<ComponentEvent>(name, linkCardinality), input: false);

    private static async ValueTask<ComponentInstance> ActivateAsync<TNode>(
        string componentType,
        ComponentActivationContext context,
        Func<ComponentActivationContext, ValueTask<ComponentNodeActivation<TNode>>> activate,
        IReadOnlyList<ComponentBinding> bindings)
        where TNode : IFlowNode
    {
        var activation = await activate(context).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Factory for component type '{componentType}' returned null.");
        var node = activation.Node
            ?? throw new InvalidOperationException($"Factory for component type '{componentType}' returned a null node.");

        try
        {
            var inputs = new List<ComponentInputPort>();
            var outputs = new List<ComponentOutputPort>();
            var events = new List<ComponentEventSource>();
            Bind(ComponentBindingRole.Input);
            Bind(ComponentBindingRole.SignalInput);
            Bind(ComponentBindingRole.Output);
            Bind(ComponentBindingRole.Events);
            return ComponentInstance.Create(
                node,
                inputs,
                outputs,
                completion: activation.Completion,
                disposeAsync: activation.DisposeAsync,
                addressableEvents: events);

            void Bind(ComponentBindingRole role)
            {
                foreach (var binding in bindings)
                {
                    if (binding.Identity.Role == role)
                        binding.Bind(node, componentType, inputs, outputs, events);
                }
            }
        }
        catch (Exception activationFailure)
        {
            try
            {
                await ComponentInstance.Create(
                        node,
                        completion: activation.Completion,
                        disposeAsync: activation.DisposeAsync)
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    $"Component type '{componentType}' activation and cleanup failed.",
                    activationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    private void SetFactory(
        Delegate registrationFactory,
        ComponentFactoryMode mode,
        Func<IReadOnlyList<ComponentBinding>, ComponentFactory> createFactory)
    {
        if (_createFactory is not null)
        {
            throw new InvalidOperationException(
                $"Component type '{Type}' already has a factory. Configure exactly one factory mode.");
        }

        _registrationFactory = registrationFactory;
        _factoryMode = mode;
        _createFactory = createFactory;
    }

    private ComponentNodeActivation<TNode> CreateNodeActivation<TNode>(TNode node)
        where TNode : IFlowNode
    {
        if (node is null)
        {
            throw new InvalidOperationException(
                $"Factory for component type '{Type}' returned a null node.");
        }

        return new ComponentNodeActivation<TNode>(node);
    }

    private void AddInstancePort(ComponentBindingRole role, ComponentPortMetadata metadata, bool input)
    {
        if (input)
            AddInput(metadata);
        else
            AddOutput(metadata);
        _bindingIdentities.Add(new ComponentBindingIdentity(role, metadata, Selector: null));
    }

    private void AddInput(ComponentPortMetadata port)
    {
        EnsureUnique(_inputs, port.Name, "input port");
        _inputs.Add(port);
        OnInputAdded(port);
    }

    private void AddOutput(ComponentPortMetadata port)
    {
        EnsureUnique(_outputs, port.Name, "output port");
        _outputs.Add(port);
        OnOutputAdded(port);
    }

    private void AddOption(ComponentOptionMetadata option)
    {
        EnsureUnique(_options, option.Name, "option");
        _options.Add(option);
        OnOptionAdded(option);
    }

    private void AddResource(ComponentResourceMetadata resource)
    {
        EnsureUnique(_resources, resource.Name, "resource");
        _resources.Add(resource);
        OnResourceAdded(resource);
    }

    private static void EnsureUnique<T>(IEnumerable<T> items, string name, string kind)
    {
        var existing = items.Any(item => string.Equals(
            item switch
            {
                ComponentPortMetadata port => port.Name,
                ComponentOptionMetadata option => option.Name,
                ComponentResourceMetadata resource => resource.Name,
                _ => throw new InvalidOperationException($"Unsupported component registration item '{typeof(T)}'.")
            },
            name,
            StringComparison.Ordinal));

        if (existing)
            throw new InvalidOperationException($"Component {kind} '{name}' is already registered.");
    }
}

internal enum ComponentFactoryMode
{
    Node,
    Instance
}
