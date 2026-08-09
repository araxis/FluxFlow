using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Authoring;

public interface IResourceDefinitionContainerBuilder
{
    ResourceGroupBuilder AddResourceGroup(string name);

    ResourceHandle AddResource(
        string name,
        string type,
        Action<ResourceDefinitionBuilder>? configure = null);

    ResourceHandle<TResource> AddResource<TResource>(
        string name,
        string type,
        Action<ResourceDefinitionBuilder>? configure = null);

    THandle AddResource<THandle>(
        string name,
        ApplicationResourceContract<THandle> resource)
        where THandle : AuthoredResourceHandle;

    THandle AddResource<TOptions, THandle>(
        string name,
        ApplicationResourceContract<TOptions, THandle> resource,
        Action<TOptions> configure)
        where TOptions : class
        where THandle : AuthoredResourceHandle;
}

internal sealed record ResourceInstanceEntry(
    string Type,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> Properties);

internal sealed class ResourceContainerState
{
    private readonly Dictionary<string, object> _entries = new(StringComparer.Ordinal);
    private readonly AuthoringScope _owner;
    private readonly ApplicationResourceContractCollection _resourceContracts;
    private readonly string[] _path;

    public ResourceContainerState(
        AuthoringScope owner,
        ApplicationResourceContractCollection resourceContracts,
        string[] path)
    {
        _owner = owner;
        _resourceContracts = resourceContracts;
        _path = path;
    }

    public ResourceGroupBuilder AddResourceGroup(string name)
    {
        _owner.EnsureMutable();
        name = DefinitionRules.RequireResourceName(name, nameof(name));
        EnsureAvailable(name);

        string[] path = [.. _path, name];
        var state = new ResourceContainerState(_owner, _resourceContracts, path);
        var builder = new ResourceGroupBuilder(state);
        _entries.Add(name, builder);
        return builder;
    }

    public ResourceHandle AddResource(
        string name,
        string type,
        Action<ResourceDefinitionBuilder>? configure)
    {
        var committed = AddResourceCore(name, type, configure);
        return new UntypedResourceHandle(
            _owner,
            ApplicationAddress.Resource([.. _path, committed.Name]),
            committed.Entry.Type);
    }

    public ResourceHandle<TResource> AddResource<TResource>(
        string name,
        string type,
        Action<ResourceDefinitionBuilder>? configure)
    {
        var committed = AddResourceCore(name, type, configure);
        return new ResourceHandle<TResource>(
            _owner,
            ApplicationAddress.Resource([.. _path, committed.Name]),
            committed.Entry.Type);
    }

    public THandle AddResource<THandle>(
        string name,
        ApplicationResourceContract<THandle> resource)
        where THandle : AuthoredResourceHandle
    {
        ArgumentNullException.ThrowIfNull(resource);
        return AddContractResourceCore(
            name,
            resource,
            configure: null,
            resource.CreateHandle);
    }

    public THandle AddResource<TOptions, THandle>(
        string name,
        ApplicationResourceContract<TOptions, THandle> resource,
        Action<TOptions> configure)
        where TOptions : class
        where THandle : AuthoredResourceHandle
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(configure);
        var options = resource.CreateOptions();
        configure(options);
        return AddContractResourceCore(
            name,
            resource,
            definition => resource.Apply(options, definition),
            resource.CreateHandle);
    }

    public IReadOnlyDictionary<string, ResourceDefinition> Build()
    {
        var definitions = new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
        foreach (var (name, entry) in _entries)
        {
            definitions.Add(name, entry switch
            {
                ResourceGroupBuilder group => new ResourceGroupDefinition(group.Build()),
                ResourceInstanceEntry instance => new ResourceInstanceDefinition(
                    instance.Type,
                    instance.Properties),
                _ => throw new InvalidOperationException(
                    $"Unsupported resource authoring entry '{entry.GetType().Name}'.")
            });
        }

        return definitions;
    }

    private (string Name, ResourceInstanceEntry Entry) AddResourceCore(
        string name,
        string type,
        Action<ResourceDefinitionBuilder>? configure)
    {
        _owner.EnsureMutable();
        name = DefinitionRules.RequireResourceName(name, nameof(name));
        type = DefinitionRules.RequireType(type, nameof(type));
        EnsureAvailable(name);

        var builder = new ResourceDefinitionBuilder(_owner);
        configure?.Invoke(builder);
        var entry = new ResourceInstanceEntry(type, builder.Commit());

        _ = new ResourceInstanceDefinition(entry.Type, entry.Properties);
        _entries.Add(name, entry);
        return (name, entry);
    }

    private THandle AddContractResourceCore<THandle>(
        string name,
        ApplicationResourceContract resource,
        Action<ResourceDefinitionBuilder>? configure,
        Func<ResourceHandle, THandle> createHandle)
        where THandle : AuthoredResourceHandle
    {
        _owner.EnsureMutable();
        name = DefinitionRules.RequireResourceName(name, nameof(name));
        EnsureAvailable(name);
        _resourceContracts.EnsureCanAdd(resource);

        var builder = new ResourceDefinitionBuilder(_owner);
        configure?.Invoke(builder);
        var entry = new ResourceInstanceEntry(resource.Type, builder.Commit());
        _ = new ResourceInstanceDefinition(entry.Type, entry.Properties);

        var definition = new UntypedResourceHandle(
            _owner,
            ApplicationAddress.Resource([.. _path, name]),
            entry.Type);
        var handle = createHandle(definition) ?? throw new InvalidOperationException(
            $"Application resource '{definition.Address}' returned no authoring handle.");

        _resourceContracts.Add(resource);
        _entries.Add(name, entry);
        return handle;
    }

    private void EnsureAvailable(string name)
    {
        if (_entries.ContainsKey(name))
            throw new ArgumentException($"Resource container contains duplicate name '{name}'.", nameof(name));
    }

    private sealed class UntypedResourceHandle : ResourceHandle
    {
        public UntypedResourceHandle(
            AuthoringScope owner,
            ApplicationAddress address,
            string type)
            : base(owner, address, type)
        {
        }
    }
}

public sealed class ResourceGroupBuilder : IResourceDefinitionContainerBuilder
{
    private readonly ResourceContainerState _state;

    internal ResourceGroupBuilder(ResourceContainerState state)
    {
        _state = state;
    }

    public ResourceGroupBuilder AddResourceGroup(string name)
        => _state.AddResourceGroup(name);

    public ResourceGroupBuilder AddResourceGroup(
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
        => _state.AddResource(name, type, configure);

    public ResourceGroupBuilder AddResource(
        string name,
        string type,
        out ResourceHandle resource)
    {
        resource = AddResource(name, type);
        return this;
    }

    public ResourceGroupBuilder AddResource(
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
        => _state.AddResource<TResource>(name, type, configure);

    public ResourceGroupBuilder AddResource<TResource>(
        string name,
        string type,
        out ResourceHandle<TResource> resource)
    {
        resource = AddResource<TResource>(name, type);
        return this;
    }

    public ResourceGroupBuilder AddResource<TResource>(
        string name,
        string type,
        Action<ResourceDefinitionBuilder> configure,
        out ResourceHandle<TResource> resource)
    {
        ArgumentNullException.ThrowIfNull(configure);
        resource = AddResource<TResource>(name, type, configure);
        return this;
    }

    public THandle AddResource<THandle>(
        string name,
        ApplicationResourceContract<THandle> resource)
        where THandle : AuthoredResourceHandle
        => _state.AddResource(name, resource);

    public ResourceGroupBuilder AddResource<THandle>(
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
        => _state.AddResource(name, resource, configure);

    public ResourceGroupBuilder AddResource<TOptions, THandle>(
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

    internal IReadOnlyDictionary<string, ResourceDefinition> Build() => _state.Build();
}
