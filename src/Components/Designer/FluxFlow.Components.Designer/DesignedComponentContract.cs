using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Designer;

public static class DesignedComponentContract
{
    public static ComponentContract<THandle> Create<THandle>(
        string type,
        Action<ComponentRegistrationBuilder> configure,
        Func<ComponentHandle, THandle> createHandle)
        where THandle : AuthoredComponentHandle
    {
        var declaration = CreateDeclaration(type, configure);
        return new DesignedContract<THandle>(
            declaration,
            createHandle);
    }

    public static ComponentContract<TOptions, THandle> Create<TOptions, THandle>(
        string type,
        Action<ComponentRegistrationBuilder> configure,
        Func<TOptions> createOptions,
        Action<TOptions, ComponentDefinitionBuilder> apply,
        Func<ComponentHandle, THandle> createHandle)
        where TOptions : class
        where THandle : AuthoredComponentHandle
    {
        var declaration = CreateDeclaration(type, configure);
        return new DesignedContract<TOptions, THandle>(
            declaration,
            createOptions,
            apply,
            createHandle);
    }

    private static ComponentDesignDeclaration CreateDeclaration(
        string type,
        Action<ComponentRegistrationBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(configure);

        var component = new ComponentRegistrationBuilder(type);
        configure(component);
        return component.CreateDeclaration();
    }

    private sealed class DesignedContract<THandle> :
        ComponentContract<THandle>,
        IDesignedComponentContract
        where THandle : AuthoredComponentHandle
    {
        internal DesignedContract(
            ComponentDesignDeclaration declaration,
            Func<ComponentHandle, THandle> createHandle)
            : base(declaration.Descriptor, createHandle)
            => Metadata = declaration.Metadata;

        public ComponentDesignMetadata Metadata { get; }
    }

    private sealed class DesignedContract<TOptions, THandle> :
        ComponentContract<TOptions, THandle>,
        IDesignedComponentContract
        where TOptions : class
        where THandle : AuthoredComponentHandle
    {
        internal DesignedContract(
            ComponentDesignDeclaration declaration,
            Func<TOptions> createOptions,
            Action<TOptions, ComponentDefinitionBuilder> apply,
            Func<ComponentHandle, THandle> createHandle)
            : base(declaration.Descriptor, createOptions, apply, createHandle)
            => Metadata = declaration.Metadata;

        public ComponentDesignMetadata Metadata { get; }
    }
}

internal interface IDesignedComponentContract
{
    ComponentDesignMetadata Metadata { get; }
}
