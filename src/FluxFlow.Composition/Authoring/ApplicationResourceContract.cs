using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Authoring;

public abstract class ApplicationResourceContract : IApplicationResourceRegistrar
{
    private protected ApplicationResourceContract(
        string type,
        IApplicationResourceRegistrar registrar)
    {
        Type = DefinitionRules.RequireType(type, nameof(type));
        Registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
    }

    public string Type { get; }

    private IApplicationResourceRegistrar Registrar { get; }

    IApplicationResourceRegistrar IApplicationResourceRegistrar.RegistrationIdentity
        => Registrar.RegistrationIdentity;

    void IApplicationResourceRegistrar.Register(ApplicationResourceRegistrationContext context)
        => Registrar.Register(context);

    public static ApplicationResourceContract<THandle> Create<THandle>(
        string type,
        IApplicationResourceRegistrar registrar,
        Func<ResourceHandle, THandle> createHandle)
        where THandle : AuthoredResourceHandle
        => new(type, registrar, createHandle);

    public static ApplicationResourceContract<TOptions, THandle> Create<TOptions, THandle>(
        string type,
        IApplicationResourceRegistrar registrar,
        Func<TOptions> createOptions,
        Action<TOptions, ResourceDefinitionBuilder> apply,
        Func<ResourceHandle, THandle> createHandle)
        where TOptions : class
        where THandle : AuthoredResourceHandle
        => new(type, registrar, createOptions, apply, createHandle);
}

public class ApplicationResourceContract<THandle> : ApplicationResourceContract
    where THandle : AuthoredResourceHandle
{
    private readonly Func<ResourceHandle, THandle> _createHandle;

    protected internal ApplicationResourceContract(
        string type,
        IApplicationResourceRegistrar registrar,
        Func<ResourceHandle, THandle> createHandle)
        : base(type, registrar)
    {
        _createHandle = createHandle ?? throw new ArgumentNullException(nameof(createHandle));
    }

    internal THandle CreateHandle(ResourceHandle resource)
        => _createHandle(resource) ?? throw new InvalidOperationException(
            $"Application resource contract '{Type}' returned no handle.");
}

public class ApplicationResourceContract<TOptions, THandle> : ApplicationResourceContract
    where TOptions : class
    where THandle : AuthoredResourceHandle
{
    private readonly Func<TOptions> _createOptions;
    private readonly Action<TOptions, ResourceDefinitionBuilder> _apply;
    private readonly Func<ResourceHandle, THandle> _createHandle;

    protected internal ApplicationResourceContract(
        string type,
        IApplicationResourceRegistrar registrar,
        Func<TOptions> createOptions,
        Action<TOptions, ResourceDefinitionBuilder> apply,
        Func<ResourceHandle, THandle> createHandle)
        : base(type, registrar)
    {
        _createOptions = createOptions ?? throw new ArgumentNullException(nameof(createOptions));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _createHandle = createHandle ?? throw new ArgumentNullException(nameof(createHandle));
    }

    internal TOptions CreateOptions()
        => _createOptions() ?? throw new InvalidOperationException(
            $"Application resource contract '{Type}' returned no options builder.");

    internal void Apply(TOptions options, ResourceDefinitionBuilder definition)
        => _apply(options, definition);

    internal THandle CreateHandle(ResourceHandle resource)
        => _createHandle(resource) ?? throw new InvalidOperationException(
            $"Application resource contract '{Type}' returned no handle.");
}
