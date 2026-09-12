using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public class RuntimeComponentRegistrationBuilder
{
    private readonly List<ComponentOptionMetadata> _options = [];
    private readonly List<ComponentResourceMetadata> _resources = [];
    private readonly List<(ComponentBindingIdentity Identity, ComponentBinding? Binding)> _ports = [];
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

        var ports = _ports.ToArray();
        var identities = ports.Select(port => port.Identity).ToArray();
        return new ComponentDescriptor(
            Type,
            _createFactory(ports.Select(port => port.Binding).OfType<ComponentBinding>().ToArray()),
            _registrationFactory,
            _factoryMode,
            identities,
            identities.Where(IsInput).Select(port => port.Metadata),
            identities.Where(port => !IsInput(port)).Select(port => port.Metadata),
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
        target._options.AddRange(_options);
        target._resources.AddRange(_resources);
        target._ports.AddRange(_ports);
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

    internal void AddInput<TNode>(
        string name,
        Func<TNode, ITargetBlock<FlowMessage>> selector,
        ComponentPortLinkCardinality linkCardinality,
        global::FluxFlow.Data.FlowValueShape? inputShape = null)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var metadata = inputShape is null
            ? ComponentPortMetadata.Create<global::FluxFlow.Data.FlowValue>(name, linkCardinality)
            : ComponentPortMetadata.CreateFlowValueInput(name, inputShape, linkCardinality);
        var identity = new ComponentBindingIdentity(ComponentBindingRole.Input, metadata, selector);
        AddPort(identity, new ComponentInputBinding<TNode>(identity, selector));
    }

    internal void AddSignalInput<TNode>(
        string name,
        Func<TNode, ITargetBlock<FlowMessage>> selector,
        ComponentPortLinkCardinality linkCardinality)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var metadata = ComponentPortMetadata.CreateSignal(name, linkCardinality);
        var identity = new ComponentBindingIdentity(ComponentBindingRole.SignalInput, metadata, selector);
        AddPort(identity, new ComponentInputBinding<TNode>(identity, selector));
    }

    internal void AddOutput<TNode>(
        string name,
        Func<TNode, ISourceBlock<FlowMessage>> selector,
        ComponentPortLinkCardinality linkCardinality)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var metadata = ComponentPortMetadata.Create<global::FluxFlow.Data.FlowValue>(name, linkCardinality);
        var identity = new ComponentBindingIdentity(ComponentBindingRole.Output, metadata, selector);
        AddPort(identity, new ComponentOutputBinding<TNode>(identity, selector));
    }

    internal void AddEvents<TNode>(
        string name,
        Func<TNode, ISourceBlock<FlowMessage>> selector,
        ComponentPortLinkCardinality linkCardinality)
        where TNode : IFlowNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var metadata = ComponentPortMetadata.Create<global::FluxFlow.Data.FlowValue>(name, linkCardinality);
        var identity = new ComponentBindingIdentity(ComponentBindingRole.Events, metadata, selector);
        AddPort(identity, new ComponentEventsBinding<TNode>(identity, selector));
    }

    internal void AddInstanceInput(string name, ComponentPortLinkCardinality linkCardinality)
        => AddInstancePort(ComponentBindingRole.Input, ComponentPortMetadata.Create<global::FluxFlow.Data.FlowValue>(name, linkCardinality));

    internal void AddInstanceInput(
        string name,
        global::FluxFlow.Data.FlowValueShape inputShape,
        ComponentPortLinkCardinality linkCardinality)
        => AddInstancePort(ComponentBindingRole.Input,
            ComponentPortMetadata.CreateFlowValueInput(name, inputShape, linkCardinality));

    internal void AddInstanceSignalInput(string name, ComponentPortLinkCardinality linkCardinality)
        => AddInstancePort(ComponentBindingRole.SignalInput, ComponentPortMetadata.CreateSignal(name, linkCardinality));

    internal void AddInstanceOutput(string name, ComponentPortLinkCardinality linkCardinality)
        => AddInstancePort(ComponentBindingRole.Output, ComponentPortMetadata.Create<global::FluxFlow.Data.FlowValue>(name, linkCardinality));

    internal void AddInstanceEvents(string name, ComponentPortLinkCardinality linkCardinality)
        => AddInstancePort(ComponentBindingRole.Events, ComponentPortMetadata.Create<global::FluxFlow.Data.FlowValue>(name, linkCardinality));

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

    private void AddInstancePort(ComponentBindingRole role, ComponentPortMetadata metadata)
        => AddPort(new ComponentBindingIdentity(role, metadata, Selector: null));

    private void AddPort(ComponentBindingIdentity identity, ComponentBinding? binding = null)
    {
        var input = IsInput(identity);
        EnsureUnique(_ports.Where(port => IsInput(port.Identity) == input).Select(port => port.Identity.Metadata.Name),
            identity.Metadata.Name, input ? "input port" : "output port");
        _ports.Add((identity, binding));
        if (input) OnInputAdded(identity.Metadata);
        else OnOutputAdded(identity.Metadata);
    }

    private static bool IsInput(ComponentBindingIdentity port)
        => port.Role is ComponentBindingRole.Input or ComponentBindingRole.SignalInput;

    private void AddOption(ComponentOptionMetadata option)
    {
        EnsureUnique(_options.Select(item => item.Name), option.Name, "option");
        _options.Add(option);
        OnOptionAdded(option);
    }

    private void AddResource(ComponentResourceMetadata resource)
    {
        EnsureUnique(_resources.Select(item => item.Name), resource.Name, "resource");
        _resources.Add(resource);
        OnResourceAdded(resource);
    }

    private static void EnsureUnique(IEnumerable<string> names, string name, string kind)
    {
        if (names.Contains(name, StringComparer.Ordinal))
            throw new InvalidOperationException($"Component {kind} '{name}' is already registered.");
    }
}

internal enum ComponentFactoryMode
{
    Node,
    Instance
}
