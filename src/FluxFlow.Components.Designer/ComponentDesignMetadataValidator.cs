using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public static class ComponentDesignMetadataValidator
{
    public static IReadOnlyList<DesignerMetadataValidationError> Validate(ComponentDesignMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var errors = new List<DesignerMetadataValidationError>();

        if (string.IsNullOrWhiteSpace(metadata.Type.Value))
        {
            errors.Add(new DesignerMetadataValidationError(
                nameof(ComponentDesignMetadata.Type),
                "Component type is required."));
        }

        ValidateOptionalText(metadata.DisplayName, nameof(ComponentDesignMetadata.DisplayName), errors);
        ValidateOptionalText(metadata.Category, nameof(ComponentDesignMetadata.Category), errors);
        ValidateOptionalText(metadata.Summary, nameof(ComponentDesignMetadata.Summary), errors);
        ValidateOptionalText(metadata.IconKey, nameof(ComponentDesignMetadata.IconKey), errors);
        ValidateOptionalText(metadata.PreferredNodeName, nameof(ComponentDesignMetadata.PreferredNodeName), errors);

        if (metadata.SuggestedEditorWidth is <= 0)
        {
            errors.Add(new DesignerMetadataValidationError(
                nameof(ComponentDesignMetadata.SuggestedEditorWidth),
                "Suggested editor width must be greater than zero when it is provided."));
        }

        ValidateOptions(metadata.Options, errors);
        ValidatePorts(metadata.Ports, errors);
        ValidateAttributes(metadata.Attributes, nameof(ComponentDesignMetadata.Attributes), errors);

        return errors;
    }

    public static void ThrowIfInvalid(ComponentDesignMetadata metadata)
    {
        var errors = Validate(metadata);
        if (errors.Count == 0)
            return;

        var message = string.Join(Environment.NewLine, errors.Select(error => $"{error.Path}: {error.Message}"));
        throw new InvalidOperationException($"Component design metadata is invalid:{Environment.NewLine}{message}");
    }

    private static void ValidateOptions(
        IReadOnlyList<OptionDesignMetadata> options,
        ICollection<DesignerMetadataValidationError> errors)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var path = $"{nameof(ComponentDesignMetadata.Options)}[{index}]";

            if (string.IsNullOrWhiteSpace(option.Name))
            {
                errors.Add(new DesignerMetadataValidationError($"{path}.{nameof(OptionDesignMetadata.Name)}", "Option name is required."));
            }
            else if (!names.Add(option.Name))
            {
                errors.Add(new DesignerMetadataValidationError($"{path}.{nameof(OptionDesignMetadata.Name)}", $"Option name '{option.Name}' is already used."));
            }

            if (option.Min > option.Max)
            {
                errors.Add(new DesignerMetadataValidationError(
                    $"{path}.{nameof(OptionDesignMetadata.Min)}",
                    "Option minimum cannot be greater than the maximum."));
            }

            ValidateOptionalText(option.DisplayName, $"{path}.{nameof(OptionDesignMetadata.DisplayName)}", errors);
            ValidateOptionalText(option.HelperText, $"{path}.{nameof(OptionDesignMetadata.HelperText)}", errors);
            ValidateChoices(option.Choices, path, errors);
            ValidateAttributes(option.Attributes, $"{path}.{nameof(OptionDesignMetadata.Attributes)}", errors);
        }
    }

    private static void ValidateChoices(
        IReadOnlyList<OptionChoiceMetadata> choices,
        string optionPath,
        ICollection<DesignerMetadataValidationError> errors)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index];
            var path = $"{optionPath}.{nameof(OptionDesignMetadata.Choices)}[{index}]";

            if (string.IsNullOrWhiteSpace(choice.Value))
            {
                errors.Add(new DesignerMetadataValidationError($"{path}.{nameof(OptionChoiceMetadata.Value)}", "Choice value is required."));
            }
            else if (!values.Add(choice.Value))
            {
                errors.Add(new DesignerMetadataValidationError($"{path}.{nameof(OptionChoiceMetadata.Value)}", $"Choice value '{choice.Value}' is already used."));
            }

            ValidateOptionalText(choice.DisplayName, $"{path}.{nameof(OptionChoiceMetadata.DisplayName)}", errors);
            ValidateOptionalText(choice.HelperText, $"{path}.{nameof(OptionChoiceMetadata.HelperText)}", errors);
            ValidateAttributes(choice.Attributes, $"{path}.{nameof(OptionChoiceMetadata.Attributes)}", errors);
        }
    }

    private static void ValidatePorts(
        IReadOnlyList<PortDesignMetadata> ports,
        ICollection<DesignerMetadataValidationError> errors)
    {
        var names = new HashSet<(PortDirection Direction, string Name)>();

        for (var index = 0; index < ports.Count; index++)
        {
            var port = ports[index];
            var path = $"{nameof(ComponentDesignMetadata.Ports)}[{index}]";

            if (string.IsNullOrWhiteSpace(port.Name.Value))
            {
                errors.Add(new DesignerMetadataValidationError($"{path}.{nameof(PortDesignMetadata.Name)}", "Port name is required."));
                continue;
            }

            var key = (port.Direction, port.Name.Value);

            if (!names.Add(key))
            {
                errors.Add(new DesignerMetadataValidationError(
                    $"{path}.{nameof(PortDesignMetadata.Name)}",
                    $"Port '{port.Name}' is already used for direction '{port.Direction}'."));
            }

            ValidateOptionalText(port.DisplayName, $"{path}.{nameof(PortDesignMetadata.DisplayName)}", errors);
            ValidateOptionalText(port.Group, $"{path}.{nameof(PortDesignMetadata.Group)}", errors);
            ValidateOptionalText(port.Summary, $"{path}.{nameof(PortDesignMetadata.Summary)}", errors);
            ValidateOptionalText(port.ValueType, $"{path}.{nameof(PortDesignMetadata.ValueType)}", errors);
            ValidateAttributes(port.Attributes, $"{path}.{nameof(PortDesignMetadata.Attributes)}", errors);
        }
    }

    private static void ValidateAttributes(
        IReadOnlyDictionary<string, string> attributes,
        string path,
        ICollection<DesignerMetadataValidationError> errors)
    {
        foreach (var attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key))
            {
                errors.Add(new DesignerMetadataValidationError(path, "Attribute keys are required."));
            }

            if (string.IsNullOrWhiteSpace(attribute.Value))
            {
                errors.Add(new DesignerMetadataValidationError($"{path}.{attribute.Key}", "Attribute values are required."));
            }
        }
    }

    private static void ValidateOptionalText(
        string? value,
        string path,
        ICollection<DesignerMetadataValidationError> errors)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            errors.Add(new DesignerMetadataValidationError(path, "Value cannot be empty when it is provided."));
    }
}
