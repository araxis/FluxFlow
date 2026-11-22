using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;

namespace FluxFlow.Studio.UI.Features.Designer.Components;

internal static class ComponentJsonValidation
{
    public static void Validate(JsonElement value, string type, ComponentAddress address, IReadOnlyDictionary<string, FluxFlow.Plugins.PropertyRules>? validators = null)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Enter a JSON object.");
        var component = JsonNode.Parse(value.GetRawText())!.AsObject();
        if (FluxFlow.Environments.ResourceJson.Text(component, "Type") != type)
            throw new ArgumentException($"Keep Type set to {type}.");
        var document = new JsonObject
        {
            ["Workflows"] = new JsonObject
            {
                [address.WorkflowName] = new JsonObject { [address.ComponentName] = component }
            }
        };
        // Existing authoring checks inspect registrations without activating runtime factories.
        var error = DesignerConfigurationValidator.Validate(document.ToJsonString(), validators: validators)
            .FirstOrDefault(message => message.Severity == ValidationSeverity.Error);
        if (error is not null) throw new ArgumentException(error.Message);
    }
}
