using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Authoring;

public abstract class ComponentContract
{
    private protected ComponentContract(ComponentDescriptor descriptor)
        => Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

    public string Type => Descriptor.Type;

    public ComponentDescriptor Descriptor { get; }

    public static ComponentContract<THandle> Create<THandle>(
        string type,
        Action<RuntimeComponentRegistrationBuilder> configureRuntime,
        Func<ComponentHandle, THandle> createHandle)
        where THandle : AuthoredComponentHandle
        => new(CreateDescriptor(type, configureRuntime), createHandle);

    public static ComponentContract<TOptions, THandle> Create<TOptions, THandle>(
        string type,
        Action<RuntimeComponentRegistrationBuilder> configureRuntime,
        Func<TOptions> createOptions,
        Action<TOptions, ComponentDefinitionBuilder> apply,
        Func<ComponentHandle, THandle> createHandle)
        where TOptions : class
        where THandle : AuthoredComponentHandle
        => new(
            CreateDescriptor(type, configureRuntime),
            createOptions,
            apply,
            createHandle);

    private static ComponentDescriptor CreateDescriptor(
        string type,
        Action<RuntimeComponentRegistrationBuilder> configureRuntime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(configureRuntime);

        var runtime = new RuntimeComponentRegistrationBuilder(type);
        configureRuntime(runtime);
        return runtime.CreateDescriptor();
    }
}

public class ComponentContract<THandle> : ComponentContract
    where THandle : AuthoredComponentHandle
{
    private readonly Func<ComponentHandle, THandle> _createHandle;

    protected internal ComponentContract(
        ComponentDescriptor descriptor,
        Func<ComponentHandle, THandle> createHandle)
        : base(descriptor)
    {
        _createHandle = createHandle ?? throw new ArgumentNullException(nameof(createHandle));
    }

    internal THandle CreateHandle(ComponentHandle component)
        => _createHandle(component) ?? throw new InvalidOperationException(
            $"Component contract '{Type}' returned no handle.");
}

public class ComponentContract<TOptions, THandle> : ComponentContract
    where TOptions : class
    where THandle : AuthoredComponentHandle
{
    private readonly Func<TOptions> _createOptions;
    private readonly Action<TOptions, ComponentDefinitionBuilder> _apply;
    private readonly Func<ComponentHandle, THandle> _createHandle;

    protected internal ComponentContract(
        ComponentDescriptor descriptor,
        Func<TOptions> createOptions,
        Action<TOptions, ComponentDefinitionBuilder> apply,
        Func<ComponentHandle, THandle> createHandle)
        : base(descriptor)
    {
        _createOptions = createOptions ?? throw new ArgumentNullException(nameof(createOptions));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _createHandle = createHandle ?? throw new ArgumentNullException(nameof(createHandle));
    }

    internal TOptions CreateOptions()
        => _createOptions() ?? throw new InvalidOperationException(
            $"Component contract '{Type}' returned no options builder.");

    internal void Apply(TOptions options, ComponentDefinitionBuilder definition)
        => _apply(options, definition);

    internal THandle CreateHandle(ComponentHandle component)
        => _createHandle(component) ?? throw new InvalidOperationException(
            $"Component contract '{Type}' returned no handle.");
}
