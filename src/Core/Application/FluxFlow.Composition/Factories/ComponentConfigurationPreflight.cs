using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Json.Model;

namespace FluxFlow.Composition;

/// <summary>Checks facts declared in metadata without constructing components or resolving resources.</summary>
internal static class ComponentConfigurationPreflight
{
    internal static void Validate(ApplicationDefinition definition, ComponentCatalog catalog)
    {
        foreach (var (workflowName, workflow) in definition.Workflows)
        foreach (var (name, component) in workflow.Components)
        {
            var descriptor = catalog.Components[component.Type];
            var address = $"{workflowName}.{name}";
            if (descriptor.Inputs.Values.Any(port => port.Kind == ComponentPortKind.Message && port.MessageType != typeof(FlowValue)) ||
                descriptor.Outputs.Values.Any(port => port.Kind != ComponentPortKind.Message || port.MessageType != typeof(FlowValue)))
                throw new InvalidOperationException($"Component '{address}' must declare native FlowValue data ports.");
            foreach (var option in descriptor.Options.Values)
            {
                if (option.IsRequired && !HasValue(component, option.Name))
                    throw new InvalidOperationException($"Component '{address}' requires option '{option.Name}'.");
            }
            foreach (var resource in descriptor.Resources.Values)
            {
                if (!HasValue(component, resource.Name))
                {
                    if (resource.IsRequired)
                        throw new InvalidOperationException($"Component '{address}' requires resource '{resource.Name}'.");
                    continue;
                }
                var value = component.Properties[resource.Name];
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw new InvalidOperationException($"Component '{address}' resource '{resource.Name}' requires a non-empty key.");
            }
        }
    }

    private static bool HasValue(ComponentDefinition component, string name)
        => component.Properties.TryGetValue(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
}
