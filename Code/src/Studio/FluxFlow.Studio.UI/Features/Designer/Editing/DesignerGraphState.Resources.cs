using System.Text.Json.Nodes;
using FluxFlow.Authoring;

namespace FluxFlow.Studio.UI.Features.Designer;

public sealed partial class DesignerGraphState
{
    public IReadOnlyList<ConfigurationResource> Connections => new DesignerConfiguration(ToJson(), SectionPath).Resources();
    public IReadOnlyList<string> ConnectionUsages(ConfigurationResource resource) =>
        new DesignerConfiguration(ToJson(), SectionPath).ResourceUsages(resource, Components);

    public void SetConnection(string name, string? workflow, JsonObject settings, bool add = false)
    {
        RequireIdleEditor();
        RecordEdit();
        var candidate = new DesignerConfiguration(ToJson(), SectionPath);
        candidate.SetResource(name, workflow, settings, add);
        ApplyEdits(new StudioEditProposal(this, Revision, candidate.ToJson(), []));
    }

    public string RenameConnection(string path, string name)
    {
        RequireIdleEditor();
        var resource = Connections.SingleOrDefault(item => item.Path == path)
            ?? throw new ArgumentException("The connection no longer exists.");
        var candidate = new DesignerConfiguration(ToJson(), SectionPath);
        var renamed = candidate.RenameResource(resource.Name, resource.Workflow, name, Components);
        if (renamed.Path == path) return path;
        var environments = _environments.Select(item => item.Copy()).ToList();
        foreach (var environment in environments)
        {
            RenameKey(environment.Setup.Resources);
            for (var index = 0; index < environment.Topology.Nodes.Count; index++)
            {
                var node = environment.Topology.Nodes[index];
                if (string.Equals(node.Resource, path, StringComparison.OrdinalIgnoreCase))
                    environment.Topology.Nodes[index] = node with { Resource = renamed.Path };
                RenameKey(node.Bindings);
            }
        }
        RecordEdit();
        ApplyEdits(new StudioEditProposal(this, Revision, candidate.ToJson(), []), () => _environments = environments);
        return renamed.Path;

        void RenameKey<T>(Dictionary<string, T> values)
        {
            var key = values.Keys.FirstOrDefault(key => key.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (key is null) return;
            if (values.Keys.Any(other => other != key && other.Equals(renamed.Path, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("The new connection name already has environment settings or bindings.");
            var value = values[key];
            values.Remove(key);
            values.Add(renamed.Path, value);
        }
    }

    public void RemoveConnection(string path)
    {
        RequireIdleEditor();
        var resource = Connections.SingleOrDefault(item => item.Path == path)
            ?? throw new ArgumentException("The connection no longer exists.");
        var candidate = new DesignerConfiguration(ToJson(), SectionPath);
        candidate.RemoveResource(resource.Name, resource.Workflow, Components);
        RecordEdit();
        ApplyEdits(new StudioEditProposal(this, Revision, candidate.ToJson(), []));
    }
}
