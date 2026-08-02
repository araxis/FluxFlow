namespace FluxFlow.Composition;

public class RuntimeComponentRegistrationBuilder
{
    private readonly List<ComponentPortMetadata> inputs = [];
    private readonly List<ComponentPortMetadata> outputs = [];
    private readonly List<ComponentOptionMetadata> options = [];
    private readonly List<ComponentResourceMetadata> resources = [];
    private ComponentFactory? factory;

    protected internal RuntimeComponentRegistrationBuilder(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Type = type.Trim();
    }

    protected string Type { get; }

    protected CompositionProcessingCapabilities ProcessingCapabilities { get; private set; } =
        CompositionProcessingCapabilities.Sequential;

    public void UseFactory(ComponentFactory value)
        => factory = value ?? throw new ArgumentNullException(nameof(value));

    public void UseProcessing(CompositionProcessingCapabilities capabilities)
    {
        if (!Enum.IsDefined(capabilities))
            throw new ArgumentOutOfRangeException(nameof(capabilities));

        ProcessingCapabilities = capabilities;
        OnProcessingChanged(capabilities);
    }

    public void AddInput<TMessage>(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
        => AddInput(ComponentPortMetadata.Create<TMessage>(name, linkCardinality));

    public void AddSignalInput(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
        => AddInput(ComponentPortMetadata.CreateSignal(name, linkCardinality));

    public void AddOutput<TMessage>(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
        => AddOutput(ComponentPortMetadata.Create<TMessage>(name, linkCardinality));

    public void AddOption<TValue>(string name, bool isRequired = false)
        => AddOption(ComponentOptions.Metadata<TValue>(name, isRequired));

    public void AddResource<TService>(
        string name,
        bool isRequired = false,
        string? valueTypeHint = null)
        => AddResource(ComponentResources.Metadata<TService>(name, isRequired, valueTypeHint));

    protected internal ComponentDescriptor CreateDescriptor()
    {
        if (factory is null)
        {
            throw new InvalidOperationException(
                $"Component type '{Type}' requires a factory. Call {nameof(UseFactory)} during registration.");
        }

        return new ComponentDescriptor(
            Type,
            factory,
            inputs,
            outputs,
            ProcessingCapabilities,
            options,
            resources);
    }

    protected internal void CopyRuntimeConfigurationTo(
        RuntimeComponentRegistrationBuilder target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (factory is not null)
            target.UseFactory(factory);

        target.UseProcessing(ProcessingCapabilities);
        foreach (var input in inputs)
            target.AddInput(input);
        foreach (var output in outputs)
            target.AddOutput(output);
        foreach (var option in options)
            target.AddOption(option);
        foreach (var resource in resources)
            target.AddResource(resource);
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

    private void AddInput(ComponentPortMetadata port)
    {
        EnsureUnique(inputs, port.Name, "input port");
        inputs.Add(port);
        OnInputAdded(port);
    }

    private void AddOutput(ComponentPortMetadata port)
    {
        if (string.Equals(port.Name, ComponentEvents.PortName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Output port '{ComponentEvents.PortName}' is reserved for component events.",
                nameof(port));
        }

        EnsureUnique(outputs, port.Name, "output port");
        outputs.Add(port);
        OnOutputAdded(port);
    }

    private void AddOption(ComponentOptionMetadata option)
    {
        EnsureUnique(options, option.Name, "option");
        options.Add(option);
        OnOptionAdded(option);
    }

    private void AddResource(ComponentResourceMetadata resource)
    {
        EnsureUnique(resources, resource.Name, "resource");
        resources.Add(resource);
        OnResourceAdded(resource);
    }

    private static void EnsureUnique<T>(
        IEnumerable<T> items,
        string name,
        string kind)
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
