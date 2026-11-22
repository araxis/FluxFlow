using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Environments;

public sealed class EnvironmentPreparation(IEnumerable<IResourceEnvironmentAdapter> adapters, IEnumerable<IEnvironmentProvider> providers,
    IEnumerable<ContainerServiceDefinition>? containers = null, Func<ContainerServiceDefinition, IEnvironmentProvider>? provision = null)
{
    private readonly Dictionary<string, IResourceEnvironmentAdapter> _adapters = adapters.ToDictionary(item => item.Type, StringComparer.Ordinal);
    private readonly Dictionary<string, IEnvironmentProvider> _providers = providers
        .Concat(provision is null ? [] : (containers ?? []).Select(provision)).ToDictionary(item => item.Name, StringComparer.Ordinal);

    public IReadOnlyList<EnvironmentRequirement> Inspect(string configuration) => ReadResources(configuration).Select(item =>
        new EnvironmentRequirement(item.Path, item.Type, _adapters.GetValueOrDefault(item.Type)?.Endpoint(item.Value))).ToArray();

    public void Validate(string configuration, EnvironmentProfile profile) => Plan(configuration, profile, out _);

    internal EnvironmentCheckResult? CheckValidation(EnvironmentRunRequest request)
    {
        string? resource = null;
        string? instance = null;
        try
        {
            ValidateBindings(request, out instance);
            instance = null;
            Plan(request.Configuration, PreparationProfile(request), out resource);
            return null;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { return new(false, error.Message) { NodeId = instance, Resource = resource }; }
    }

    public void ValidateBindings(EnvironmentRunRequest request) => ValidateBindings(request, out _);

    public EnvironmentProfile PreparationProfile(EnvironmentRunRequest request)
    {
        request.Environment.Validate();
        var profile = request.Environment.Copy();
        foreach (var path in profile.Resources.Keys.ToArray())
            if (request.Instances.Count > 0 && request.Instances.All(instance =>
                    instance.ExistingServices.Keys.Any(key => key.Equals(path, StringComparison.OrdinalIgnoreCase))) &&
                !request.Instances.Any(instance => instance.Bindings.Values.Any(value => value.Equals(path, StringComparison.OrdinalIgnoreCase))))
                profile.Resources[path] = new();
        return profile;
    }

    private void ValidateBindings(EnvironmentRunRequest request, out string? instanceId)
    {
        instanceId = null;
        request.ValidateInstances();
        var resources = ReadResources(request.Configuration).ToDictionary(resource => resource.Path, StringComparer.OrdinalIgnoreCase);
        var owners = new HashSet<(string Type, string Key)>();
        foreach (var instance in request.Instances)
        {
            instanceId = instance.Id;
            if (instance.Bindings is null || instance.Bindings.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != instance.Bindings.Count)
                throw new ArgumentException($"Instance '{instance.Name}' has invalid connection bindings.");
            foreach (var binding in instance.Bindings)
                if (!resources.TryGetValue(binding.Key, out var target) ||
                    !resources.TryGetValue(binding.Value, out var source) ||
                    !_adapters.ContainsKey(target.Type) || target.Type != source.Type)
                    throw new ArgumentException($"Instance '{instance.Name}' cannot bind '{binding.Key}' to '{binding.Value}'. Choose a compatible connection type.");
            if (instance.ExistingServices.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != instance.ExistingServices.Count)
                throw new ArgumentException("Existing service bindings must use distinct connection paths.");
            foreach (var binding in instance.ExistingServices)
            {
                binding.Value.Validate(_adapters.GetValueOrDefault(binding.Value.Type)?.Infrastructure);
                if (!resources.TryGetValue(binding.Key, out var target) || target.Type != binding.Value.Type ||
                    !_adapters.TryGetValue(target.Type, out var adapter) || instance.Bindings.Keys.Any(path => path.Equals(binding.Key, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Instance '{instance.Name}' cannot bind '{binding.Key}' to existing service '{binding.Value.Name}'.");
                adapter.ApplyExisting((JsonObject)target.Value.DeepClone(), binding.Value);
            }
            foreach (var resource in resources.Values)
            {
                var existing = instance.ExistingServices.FirstOrDefault(pair => pair.Key.Equals(resource.Path, StringComparison.OrdinalIgnoreCase)).Value;
                var source = instance.Bindings.FirstOrDefault(pair => pair.Key.Equals(resource.Path, StringComparison.OrdinalIgnoreCase)).Value;
                if (!_adapters.TryGetValue(resource.Type, out var adapter)) continue;
                var endpoint = existing?.Endpoint ?? adapter.Endpoint(source is null ? resource.Value : resources[source].Value);
                if (endpoint is null) continue;
                if (adapter.ExclusiveKey(new Uri(endpoint)) is { } key && !owners.Add((resource.Type, key)))
                    throw new ArgumentException($"Instance '{instance.Name}' uses {resource.Type} '{key}', which already has an owner. Bind each instance to a different device.");
            }
        }
    }

    public string BindConfiguration(PreparedEnvironment environment, ApplicationInstance instance)
    {
        var copy = (JsonObject)environment.Configuration.DeepClone();
        foreach (var binding in instance.Bindings)
        {
            var source = Find(environment.Configuration, binding.Value);
            var target = Find(copy, binding.Key);
            target.Clear();
            foreach (var property in source) target[property.Key] = property.Value?.DeepClone();
        }
        foreach (var binding in instance.ExistingServices)
        {
            binding.Value.Validate(_adapters.GetValueOrDefault(binding.Value.Type)?.Infrastructure);
            var target = Find(copy, binding.Key);
            var original = Find(environment.OriginalConfiguration, binding.Key);
            target.Clear();
            foreach (var property in original) target[property.Key] = property.Value?.DeepClone();
            if (ResourceJson.Text(target, "Type") != binding.Value.Type)
                throw new ArgumentException("Existing service binding type no longer matches the connection.");
            _adapters[binding.Value.Type].ApplyExisting(target, binding.Value);
        }
        return copy.ToJsonString();
    }

    public async Task<PreparedEnvironment> PrepareAsync(string configuration, EnvironmentProfile profile, CancellationToken cancellationToken = default)
    {
        var snapshot = profile.Copy();
        var plan = Plan(configuration, snapshot, out _);
        var result = new PreparedEnvironment(JsonNode.Parse(configuration)!.AsObject());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(snapshot.StartupTimeoutSeconds));
        try
        {
            foreach (var item in plan)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (item.Provider is null)
                {
                    result.Add(new(item.Resource.Path, "existing", item.Adapter.Endpoint(item.Resource.Value) ?? "From request", null));
                    continue;
                }
                var service = await item.Provider.StartAsync(item.Binding, deadline.Token).ConfigureAwait(false);
                result.Own(service);
                var target = Find(result.Configuration, item.Resource.Path);
                item.Adapter.Apply(target, service.Endpoint, item.Binding);
                result.Add(new(item.Resource.Path, item.Provider.Name, service.Endpoint.AbsoluteUri, service.ContainerId));
            }
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception original)
        {
            try { await result.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Environment preparation and cleanup failed.", original, cleanup); }
            throw;
        }
    }

    private List<PlannedResource> Plan(string configuration, EnvironmentProfile profile, out string? resourcePath)
    {
        resourcePath = null;
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        var resources = ReadResources(configuration);
        foreach (var path in profile.Resources.Keys)
            if (!resources.Any(resource => resource.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                resourcePath = path;
                throw new ArgumentException($"Environment resource '{path}' no longer exists.");
            }
        var plan = new List<PlannedResource>();
        foreach (var resource in resources)
        {
            resourcePath = resource.Path;
            if (!_adapters.TryGetValue(resource.Type, out var adapter)) throw new ArgumentException($"Resource '{resource.Path}' ({resource.Type}) is not supported by this runner.");
            var binding = profile.Resources.FirstOrDefault(pair => pair.Key.Equals(resource.Path, StringComparison.OrdinalIgnoreCase)).Value ?? new();
            IEnvironmentProvider? provider = null;
            if (binding.Provider != "existing")
            {
                if (!_providers.TryGetValue(binding.Provider, out provider) || provider.ResourceType != resource.Type)
                    throw new ArgumentException($"Provider '{binding.Provider}' cannot supply '{resource.Path}' ({resource.Type}).");
                provider.Validate(binding);
            }
            else if (binding.Image is not null || binding.Files.Count != 0 || binding.Variables.Count != 0 || binding.Mappings != "[]")
                throw new ArgumentException($"Existing resource '{resource.Path}' cannot have container setup.");
            plan.Add(new(resource, adapter, binding, provider));
        }
        return plan;
    }

    private sealed record Resource(string Path, string Type, JsonObject Value);
    private sealed record PlannedResource(Resource Resource, IResourceEnvironmentAdapter Adapter, ResourceBinding Binding, IEnvironmentProvider? Provider);
    private static List<Resource> ReadResources(string configuration)
    {
        var root = JsonNode.Parse(configuration)?.AsObject() ?? throw new ArgumentException("An application object is required.");
        if (ResourceJson.Value(root, "Workflows") is not JsonObject workflows) throw new ArgumentException("An application with Workflows is required.");
        var result = new List<Resource>();
        Visit(ResourceJson.Value(root, "Resources") as JsonObject, ResourceJson.Key(root, "Resources"));
        foreach (var workflow in workflows)
            if (workflow.Value is JsonObject value) Visit(ResourceJson.Value(value, "Resources") as JsonObject, $"{ResourceJson.Key(root, "Workflows")}:{workflow.Key}:{ResourceJson.Key(value, "Resources")}");
        return result;
        void Visit(JsonObject? section, string path)
        {
            if (section is null) return;
            foreach (var pair in section)
            {
                if (pair.Value is not JsonObject value) throw new ArgumentException($"Resource section '{path}:{pair.Key}' must be an object.");
                var childPath = $"{path}:{pair.Key}";
                if (ResourceJson.Text(value, "Type") is { } type) result.Add(new(childPath, type, value));
                else Visit(value, childPath);
            }
        }
    }
    private static JsonObject Find(JsonObject root, string path)
    {
        JsonNode? value = root;
        foreach (var part in path.Split(':')) value = ResourceJson.Value(value!.AsObject(), part);
        return value!.AsObject();
    }
}

public sealed class PreparedEnvironment(JsonObject configuration) : IAsyncDisposable
{
    private readonly List<IEnvironmentService> _owned = [];
    private readonly List<PreparedService> _services = [];
    public JsonObject Configuration { get; } = configuration;
    internal JsonObject OriginalConfiguration { get; } = (JsonObject)configuration.DeepClone();
    public IReadOnlyList<PreparedService> Services => _services.AsReadOnly();
    internal void Own(IEnvironmentService service) => _owned.Add(service);
    internal void Add(PreparedService service) => _services.Add(service);
    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];
        foreach (var service in _owned.AsEnumerable().Reverse().ToArray())
            try { await service.DisposeAsync().ConfigureAwait(false); _owned.Remove(service); }
            catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Environment cleanup failed.", failures);
    }
}

public static class EnvironmentRegistration
{
    public static IServiceCollection AddFlowEnvironments(this IServiceCollection services)
    {
        return services.AddBaseFlowEnvironments();
    }

    public static IServiceCollection AddBaseFlowEnvironments(this IServiceCollection services)
    {
        return services
            .AddSingleton<EnvironmentPreparation>()
            .AddSingleton<EnvironmentCheck>();
    }
}
