using System.Text.Json;
using FluxFlow.Core.Addressing;
using FluxFlow.Json.Model;

namespace FluxFlow.Composition.Definition;

internal sealed record ResourceInstanceEntry(
    string Type,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> Properties);

internal sealed class ResourceContainerState
{
    private readonly Dictionary<string, ResourceInstanceEntry> _entries = new(StringComparer.Ordinal);
    private readonly DefinitionScope _owner;
    private readonly string? _workflow;
    private readonly ApplicationResourceContractCollection _resourceContracts;
    public ResourceContainerState(
        DefinitionScope owner,
        ApplicationResourceContractCollection resourceContracts, string? workflow = null)
    {
        _owner = owner;
        _workflow = workflow;
        _resourceContracts = resourceContracts;
    }

    public ResourceHandle AddResource(
        string name,
        string type,
        Action<ResourceDefinitionBuilder>? configure)
    {
        var committed = AddResourceCore(name, type, configure);
        return new UntypedResourceHandle(
            _owner,
            Address(committed.Name),
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
            Address(committed.Name),
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
        var root = new System.Text.Json.Nodes.JsonObject();
        foreach (var (name, entry) in _entries)
        {
            var path = name.Split('.');
            var target = root;
            foreach (var segment in path.SkipLast(1))
            {
                if (target[segment] is null) target[segment] = new System.Text.Json.Nodes.JsonObject();
                target = (System.Text.Json.Nodes.JsonObject)target[segment]!;
            }
            var value = new ResourceInstanceDefinition(entry.Type, entry.Properties).Value;
            target.Add(path[^1], System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText()));
        }
        return ApplicationDefinitionJsonConverter.ReadResourceMap(
            JsonSerializer.SerializeToElement(root), "Resources").ToDictionary(p => p.Key, p => p.Value);
    }

    private (string Name, ResourceInstanceEntry Entry) AddResourceCore(
        string name,
        string type,
        Action<ResourceDefinitionBuilder>? configure)
    {
        _owner.EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var segment in ApplicationAddress.Resource(name.Split('.')).Segments.Skip(1)) DefinitionRules.RequireResourceName(segment, nameof(name));
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
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var segment in ApplicationAddress.Resource(name.Split('.')).Segments.Skip(1)) DefinitionRules.RequireResourceName(segment, nameof(name));
        EnsureAvailable(name);
        _resourceContracts.EnsureCanAdd(resource);

        var builder = new ResourceDefinitionBuilder(_owner);
        configure?.Invoke(builder);
        var entry = new ResourceInstanceEntry(resource.Type, builder.Commit());
        _ = new ResourceInstanceDefinition(entry.Type, entry.Properties);

        var definition = new UntypedResourceHandle(
            _owner,
            Address(name),
            entry.Type);
        var handle = createHandle(definition) ?? throw new InvalidOperationException(
            $"Application resource '{definition.Address}' returned no definition handle.");

        _resourceContracts.Add(resource);
        _entries.Add(name, entry);
        return handle;
    }

    private ApplicationAddress Address(string name) => _workflow is null
        ? ApplicationAddress.Resource(name.Split('.'))
        : ApplicationAddress.WorkflowResource(_workflow, name.Split('.'));

    private void EnsureAvailable(string name)
    {
        if (_entries.Keys.Any(existing => existing == name || existing.StartsWith(name + '.', StringComparison.Ordinal) || name.StartsWith(existing + '.', StringComparison.Ordinal)))
            throw new ArgumentException($"Resource container contains duplicate name '{name}'.", nameof(name));
    }

    private sealed class UntypedResourceHandle : ResourceHandle
    {
        public UntypedResourceHandle(
            DefinitionScope owner,
            ApplicationAddress address,
            string type)
            : base(owner, address, type)
        {
        }
    }
}
