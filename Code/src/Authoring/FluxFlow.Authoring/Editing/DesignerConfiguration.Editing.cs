using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Application.Addressing;

namespace FluxFlow.Authoring;

public sealed partial class DesignerConfiguration
{
    /// <summary>
    /// Applies an ordered batch to a detached copy. Failure never changes this document.
    /// Checks configuration structure and references, not runtime port availability or component activation.
    /// </summary>
    public DesignerConfiguration PrepareEdits(IEnumerable<ConfigurationEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var candidate = new DesignerConfiguration(ToJson(), _sectionPath);
        foreach (var edit in edits)
        {
            switch (edit)
            {
                case AddComponentEdit add:
                    candidate.AddComponent(add);
                    break;
                case ConfigureComponentEdit configure:
                    candidate.ConfigureComponent(configure);
                    break;
                case ConnectPortsEdit connect:
                    candidate.ConnectPorts(connect);
                    break;
                default:
                    throw new ArgumentException("An edit must be add, configure or connect.", nameof(edits));
            }
        }
        // Reuse the configuration provider's duplicate/case-collision and structural checks.
        return new DesignerConfiguration(candidate.ToJson(), _sectionPath);
    }

    private void AddComponent(AddComponentEdit edit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edit.Type);
        var address = edit.Address;
        if (address.ComponentName.Equals("Resources", StringComparison.OrdinalIgnoreCase) ||
            address.ComponentName.Contains(':'))
            throw new ArgumentException("That component name is reserved or contains ':'.");
        EnsureWorkflow(address.WorkflowName);
        var workflow = Object(_workflows, address.WorkflowName, exact: true);
        if (workflow.Any(item => item.Key.Equals(address.ComponentName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Component '{address}' already exists.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (edit.Properties is not null)
            foreach (var (key, value) in edit.Properties)
            {
                CheckProperty(key);
                values.Add(key, value.Clone());
            }
        UpdateNode(new ConfigurationNode(address, edit.Type, values));
    }

    private void ConfigureComponent(ConfigureComponentEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit.Properties);
        var values = Component(edit.Address);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in edit.Properties)
        {
            CheckProperty(name);
            if (!names.Add(name)) throw new ArgumentException($"Property '{name}' occurs more than once.");
            var key = Key(values, name);
            if (value is null) values.Remove(key);
            else values[key] = JsonNode.Parse(value.Value.GetRawText());
        }
    }

    private void ConnectPorts(ConnectPortsEdit edit)
    {
        var source = edit.Source.ComponentAddress
            ?? throw new ArgumentException("A new connection requires a component output.");
        var target = edit.Target.ComponentAddress
            ?? throw new ArgumentException("A new connection requires a component input.");
        var owner = Component(source);
        _ = Component(target);
        if (edit.IsError && edit.Source.PortName != "Output")
            throw new ArgumentException("Error connections filter the Output port.");
        AddLink(edit.Source, edit.Target, isError: edit.IsError);
        var property = Key(owner, edit.IsError ? "Error" : edit.Source.PortName);
        var declaration = owner[property] is JsonArray array ? array.Last()! : owner[property]!;
        UpdateLink(new ConfigurationLink(edit.Source, edit.Target, null, null, edit.IsError,
            source, property, declaration, false), edit.Condition, edit.Engine);
    }

    private JsonObject Component(ComponentAddress address)
    {
        if (address.ComponentName.Equals("Resources", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Workflow resources are not a component.", nameof(address));
        return Object(Object(_workflows, address.WorkflowName, exact: true), address.ComponentName, exact: true);
    }

    private static void CheckProperty(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Equals("Type", StringComparison.OrdinalIgnoreCase) || name.Contains(':'))
            throw new ArgumentException("Type is specified when adding a component; property names cannot contain ':'.");
    }
}
