using System.Text.Json;
using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Environments.Topology;

namespace FluxFlow.Studio.UI.Features.Designer;

public sealed partial class DesignerGraphState
{
    private List<EnvironmentDefinition> _environments = [new() { Id = "local" }];
    public string ActiveEnvironmentId { get; private set; } = "local";
    private EnvironmentDefinition ActiveEnvironment => _environments.Single(item => item.Id == ActiveEnvironmentId);
    public string EnvironmentName => ActiveEnvironment.Name;
    public IReadOnlyList<EnvironmentDefinition> Environments => _environments.Select(item => item.Copy()).ToArray();
    public EnvironmentDefinition GetEnvironmentDefinition() => ActiveEnvironment.Copy();
    public EnvironmentRunRequest CreateEnvironmentRunRequest() => CreateEnvironmentRunRequest(ActiveEnvironment);

    private EnvironmentRunRequest CreateEnvironmentRunRequest(EnvironmentDefinition definition)
    {
        var services = definition.Topology.Nodes.Where(node => node.Kind == EnvironmentNodeKind.Service)
            .ToDictionary(node => node.Id, StringComparer.Ordinal);
        return new(ExportConfiguration(), definition.Setup.Copy())
        {
            Instances = definition.Topology.Nodes.Where(node => node.Kind == EnvironmentNodeKind.Application).Select(CreateInstance).ToArray()
        };

        ApplicationInstance CreateInstance(EnvironmentNode node)
        {
            var instance = new ApplicationInstance(node.Id, node.Name);
            foreach (var (path, target) in node.Bindings)
            {
                if (target is null || !services.TryGetValue(target, out var service))
                    throw new InvalidOperationException($"'{node.Name}' connection '{path}' is unbound. Choose a service in Environment topology.");
                if (service.ExistingService is { } existing) instance.ExistingServices.Add(path, existing with { });
                else instance.Bindings.Add(path, service.Resource!);
            }
            return instance;
        }
    }

    public async Task<string> ExportCompiledConfigurationAsync(string environmentId)
    {
        var definition = _environments.Single(item => item.Id == environmentId);
        if (TopologyConnections.Check(definition.Topology, Connections, ResourceAdapters) is { } problem)
            throw new InvalidOperationException(problem.Message);
        var request = CreateEnvironmentRunRequest(definition);
        if (request.Instances.Count > 1)
            throw new InvalidOperationException("Choose a Run environment with a single application before publishing a compiled application.");
        var preparation = new EnvironmentPreparation(ResourceAdapters, []);
        var profile = preparation.PreparationProfile(request);
        var managed = profile.Resources.FirstOrDefault(pair => pair.Value.Provider != "existing");
        if (managed.Key is not null)
            throw new InvalidOperationException($"'{managed.Key}' requires provisioning. Choose an existing service or a manual connection before compiling.");
        var instance = request.Instances.SingleOrDefault();
        if (instance is null || instance.Bindings.Count == 0 && instance.ExistingServices.Count == 0)
            return request.Configuration;
        preparation.ValidateBindings(request);
        await using var environment = await preparation.PrepareAsync(request.Configuration, profile);
        return preparation.BindConfiguration(environment, instance);
    }

    private string EnvironmentJson => JsonSerializer.Serialize(_environments, EnvironmentJsonContext.Default.ListEnvironmentDefinition);

    private void ResetEnvironments()
    {
        if (!_applyingEdits) ProjectGeneration++;
        _environments = [new() { Id = "local" }];
        ActiveEnvironmentId = "local";
    }

    private void RestoreEnvironments(string json)
    {
        var selected = ActiveEnvironmentId;
        _environments = JsonSerializer.Deserialize(json, EnvironmentJsonContext.Default.ListEnvironmentDefinition)!;
        foreach (var environment in _environments)
            environment.Setup.Resources = new(environment.Setup.Resources, StringComparer.OrdinalIgnoreCase);
        ActiveEnvironmentId = _environments.Any(item => item.Id == selected) ? selected : _environments[0].Id;
    }

    public void SelectEnvironment(string id)
    {
        if (!_environments.Any(item => item.Id == id)) throw new ArgumentException("Environment no longer exists.");
        if (ActiveEnvironmentId == id) return;
        ActiveEnvironmentId = id;
        Revision++;
        NotifyChanged();
    }

    internal string ValidateNewEnvironmentName(string name) => EnvironmentDefinition.ValidateNewName(name, _environments);

    public void AddEnvironment(string name)
    {
        var definition = EnvironmentDefinition.Create(name, _environments);
        using var edit = BeginManualEdit();
        _environments.Add(definition);
        ActiveEnvironmentId = definition.Id;
        RecordEdit();
    }

    public void RenameEnvironment(string name)
    {
        var current = ActiveEnvironment;
        var renamed = current.Rename(name, _environments);
        if (ReferenceEquals(current, renamed)) return;
        using var edit = BeginManualEdit();
        _environments[_environments.IndexOf(current)] = renamed;
        RecordEdit();
    }

    public void SetTopology(EnvironmentTopology topology)
    {
        ActiveEnvironment.Topology.ValidateEdit(topology, ResourceAdapters);
        using var edit = BeginManualEdit();
        var candidate = ActiveEnvironment with { Topology = topology };
        _environments[_environments.IndexOf(ActiveEnvironment)] = candidate.Copy();
        RecordEdit();
    }

    public string? EnvironmentLimitation() => CheckTopology()?.Message;

    public void SetLocalExecutionHost(string hostId, bool local)
    {
        var topology = GetEnvironmentDefinition().Topology;
        if (topology.SetLocalExecutionHost(hostId, local)) SetTopology(topology);
    }

    internal EnvironmentCheckResult? CheckTopology()
    {
        var definition = ActiveEnvironment;
        var connections = Connections;
        if (TopologyConnections.Check(definition.Topology, connections, ResourceAdapters) is { } bindingError) return bindingError;
        foreach (var node in definition.Topology.Nodes)
        {
            if (node.Kind == EnvironmentNodeKind.Service && node.ExistingService is null && !connections.Any(item => item.Path.Equals(node.Resource, StringComparison.OrdinalIgnoreCase)))
                return new(false, $"Service '{node.Name}' refers to a connection that no longer exists. Remove it from the topology or restore its connection.") { NodeId = node.Id };
            var container = node.Resource is not null && definition.Setup.Resources.TryGetValue(node.Resource, out var setup) && setup.Provider != "existing";
            if ((node.Kind == EnvironmentNodeKind.Application || container) && node.Host is not null &&
                definition.Topology.Nodes.Any(host => host.Id == node.Host && !host.LocalHost))
                return new(false, $"'{node.Name}' is placed on another host. This companion currently runs applications and containers on this computer only.") { NodeId = node.Id };
        }
        return null;
    }
}
