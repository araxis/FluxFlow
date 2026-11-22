using FluxFlow.Application.Addressing;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Authoring;

/// <summary>Checks supported component registrations without building a provider or activating factories.</summary>
public static class DesignerConfigurationValidator
{
    public static IReadOnlyList<ValidationMessageModel> Validate(string json, string sectionPath = "", IReadOnlyDictionary<string, FluxFlow.Plugins.PropertyRules>? validators = null)
    {
        var messages = new List<ValidationMessageModel>();
        try
        {
            // Reuse the editor's structural checks and the standard configuration provider.
            var document = new DesignerConfiguration(json, sectionPath);
            using var configuration = document.ReadConfiguration();
            IConfiguration application = sectionPath.Length == 0 ? configuration : configuration.GetSection(sectionPath);
            ReportResources(application.GetSection("Resources"));
            foreach (var workflow in document.WorkflowNames)
            {
                var section = application.GetSection("Workflows").GetSection(workflow);
                ReportResources(section.GetSection("Resources"));
                foreach (var node in document.Nodes(workflow))
                {
                    var component = section.GetSection(node.Address.ComponentName);
                    try
                    {
                        if (validators?.GetValueOrDefault(node.Type) is { } rules)
                        {
                            var note = rules.ValidationNote?.Invoke(component);
                            if (note is { IsWarning: true }) Add(ValidationSeverity.Warning, note.Message, component.Path, node.Address);
                            rules.Validate?.Invoke(component);
                            if (note is { IsWarning: false }) Add(ValidationSeverity.Information, note.Message, component.Path, node.Address);
                            continue;
                        }
                        Add(ValidationSeverity.Warning, $"Not validated: component type '{node.Type}' is unavailable in this authoring host.", component.Path, node.Address);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        Add(ValidationSeverity.Error, error.Message, component.Path, node.Address);
                    }
                }
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Add(ValidationSeverity.Error, error.Message, sectionPath);
        }
        return messages;

        void Add(ValidationSeverity severity, string message, string path, ComponentAddress? address = null)
            => messages.Add(new ValidationMessageModel { Severity = severity, Source = ValidationSource.Composition,
                Message = message, Path = path, Address = address });

        void ReportResources(IConfigurationSection section)
        {
            if (section.Exists())
                Add(ValidationSeverity.Warning, "Not validated: resources and their dependencies are supplied by the runner.", section.Path);
        }
    }

}
