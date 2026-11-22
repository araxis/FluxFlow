namespace FluxFlow.Environments;

// A missing entry uses the configured connection. An explicit null disconnects it.
public readonly record struct TopologyBinding
{
    private TopologyBinding(string? target) { IsExplicit = true; Target = target; }
    public bool IsExplicit { get; }
    public string? Target { get; }
    public static TopologyBinding Configured => default;
    public static TopologyBinding Explicit(string? target) => new(target);
}

public sealed partial record EnvironmentTopology
{
    public static TopologyBinding GetBinding(EnvironmentNode app, string path)
    {
        var entry = app.Bindings.FirstOrDefault(pair => pair.Key.Equals(path, StringComparison.OrdinalIgnoreCase));
        return entry.Key is null ? TopologyBinding.Configured : TopologyBinding.Explicit(entry.Value);
    }

    public string? BindingTarget(EnvironmentNode app, string path) => GetBinding(app, path) is { IsExplicit: true } binding
        ? binding.Target : Nodes.FirstOrDefault(node => node.Kind == EnvironmentNodeKind.Service &&
            string.Equals(node.Resource, path, StringComparison.OrdinalIgnoreCase))?.Id;

    public static string? ServiceType(EnvironmentNode service, Func<string, string?> connectionType) =>
        service.ExistingService?.Type ?? (service.Resource is { } path ? connectionType(path) : null);

    public bool SetBinding(string applicationId, string path, TopologyBinding binding, Func<string, string?> connectionType)
    {
        var app = Nodes.Find(node => node.Id == applicationId && node.Kind == EnvironmentNodeKind.Application);
        if (app is null || connectionType(path) is not { Length: > 0 } type)
            throw new ArgumentException("The application or its connection is no longer available.");
        if (binding.Target is { } id && (Nodes.Find(node => node.Id == id && node.Kind == EnvironmentNodeKind.Service) is not { } service ||
            ServiceType(service, connectionType) != type))
            throw new ArgumentException($"'{path}' requires a compatible service.");
        if (GetBinding(app, path) == binding) return false;
        var bindings = new Dictionary<string, string?>(app.Bindings, StringComparer.OrdinalIgnoreCase);
        if (binding.IsExplicit) bindings[path] = binding.Target;
        else bindings.Remove(path);
        app.Bindings = bindings;
        return true;
    }
}
