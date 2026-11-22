namespace FluxFlow.Authoring;

public sealed partial class DesignerConfiguration
{
    /// <summary>
    /// Inspects one workflow using descriptions resolved through the caller's DI scope.
    /// Does not apply creation defaults, validate runtime registrations or activate components.
    /// </summary>
    public WorkflowInspection InspectWorkflow(string workflow, IEnumerable<IComponentDescription> descriptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflow);
        ArgumentNullException.ThrowIfNull(descriptions);
        var registered = descriptions.ToDictionary(item => item.Palette.ComponentType, StringComparer.Ordinal);
        // ConfigurationLink retains its declaration node. Work on a copy so neither that
        // node nor its parents can be used to mutate the caller's retained configuration.
        var snapshot = new DesignerConfiguration(ToJson(), _sectionPath);
        var components = snapshot.Nodes(workflow).Select(node =>
        {
            registered.TryGetValue(node.Type, out var description);
            return new ComponentInspection(node, description?.GetPresentation(node.Values), description?.GetInspector(node.Values));
        }).ToArray();
        var links = snapshot.ReadLinks(node => registered.GetValueOrDefault(node.Type)?.GetPresentation(node.Values))
            .Where(link => link.Source.ComponentAddress?.WorkflowName == workflow ||
                link.Target.ComponentAddress?.WorkflowName == workflow).ToArray();
        // Unknown declarations in other workflows may contain incoming links too.
        var diagnostics = snapshot.WorkflowNames.SelectMany(snapshot.Nodes)
            .Where(node => !registered.ContainsKey(node.Type))
            .Select(node => new ValidationMessageModel
            {
                Severity = ValidationSeverity.Warning,
                Source = ValidationSource.Composition,
                Address = node.Address,
                Message = $"Link inspection is incomplete: '{node.Address}' uses '{node.Type}', which has no registered description. Its configuration is preserved."
            }).ToArray();
        return new WorkflowInspection(workflow, Array.AsReadOnly(components), Array.AsReadOnly(links), Array.AsReadOnly(diagnostics));
    }
}
