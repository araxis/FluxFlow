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
        ValidateResources(metadata.Resources, errors);
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
        IReadOnlyList<OptionDesignMetadata>? options,
        ICollection<DesignerMetadataValidationError> errors)
    {
        if (options is null)
        {
            errors.Add(new DesignerMetadataValidationError(
                nameof(ComponentDesignMetadata.Options),
                "Options collection is required."));
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var path = $"{nameof(ComponentDesignMetadata.Options)}[{index}]";

            if (option is null)
            {
                errors.Add(new DesignerMetadataValidationError(path, "Option metadata is required."));
                continue;
            }

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

            ValidateOptionRangeUsage(option, path, errors);
            ValidateOptionDefaultValue(option, path, errors);
            ValidateOptionalText(option.DisplayName, $"{path}.{nameof(OptionDesignMetadata.DisplayName)}", errors);
            ValidateOptionalText(option.HelperText, $"{path}.{nameof(OptionDesignMetadata.HelperText)}", errors);
            if (option.Choices is null)
            {
                errors.Add(new DesignerMetadataValidationError(
                    $"{path}.{nameof(OptionDesignMetadata.Choices)}",
                    "Choices collection is required."));
            }
            else
            {
                ValidateChoiceUsage(option, path, errors);
                ValidateChoices(option.Choices, path, errors);
            }

            ValidateAttributes(option.Attributes, $"{path}.{nameof(OptionDesignMetadata.Attributes)}", errors);
        }
    }

    private static void ValidateOptionRangeUsage(
        OptionDesignMetadata option,
        string optionPath,
        ICollection<DesignerMetadataValidationError> errors)
    {
        if (option.Min is null && option.Max is null)
            return;

        if (option.Kind is OptionValueKind.Number or OptionValueKind.Duration)
            return;

        errors.Add(new DesignerMetadataValidationError(
            $"{optionPath}.{nameof(OptionDesignMetadata.Min)}",
            "Option min/max constraints are valid only for number and duration options."));
    }

    private static void ValidateOptionDefaultValue(
        OptionDesignMetadata option,
        string optionPath,
        ICollection<DesignerMetadataValidationError> errors)
    {
        if (option.DefaultValue is null)
            return;

        var defaultValuePath = $"{optionPath}.{nameof(OptionDesignMetadata.DefaultValue)}";

        switch (option.Kind)
        {
            case OptionValueKind.Text:
            case OptionValueKind.MultilineText:
            case OptionValueKind.Expression:
            case OptionValueKind.Secret:
                ValidateDefaultValueType<string>(option.DefaultValue, defaultValuePath, option.Kind, errors);
                break;
            case OptionValueKind.Number:
                if (!IsNumericDefaultValue(option.DefaultValue))
                {
                    errors.Add(new DesignerMetadataValidationError(
                        defaultValuePath,
                        $"Default value for {option.Kind} options must be numeric."));
                }

                break;
            case OptionValueKind.Boolean:
                ValidateDefaultValueType<bool>(option.DefaultValue, defaultValuePath, option.Kind, errors);
                break;
            case OptionValueKind.Duration:
                ValidateDefaultValueType<TimeSpan>(option.DefaultValue, defaultValuePath, option.Kind, errors);
                break;
            case OptionValueKind.Enum:
                ValidateEnumDefaultValue(option, defaultValuePath, errors);
                break;
            case OptionValueKind.Json:
                break;
        }
    }

    private static void ValidateDefaultValueType<TValue>(
        object defaultValue,
        string defaultValuePath,
        OptionValueKind kind,
        ICollection<DesignerMetadataValidationError> errors)
    {
        if (defaultValue is TValue)
            return;

        errors.Add(new DesignerMetadataValidationError(
            defaultValuePath,
            $"Default value for {kind} options must be {typeof(TValue).Name}."));
    }

    private static void ValidateEnumDefaultValue(
        OptionDesignMetadata option,
        string defaultValuePath,
        ICollection<DesignerMetadataValidationError> errors)
    {
        var defaultValue = option.DefaultValue switch
        {
            string value => value,
            Enum value => value.ToString(),
            _ => null
        };

        if (defaultValue is null)
        {
            errors.Add(new DesignerMetadataValidationError(
                defaultValuePath,
                "Default value for enum options must be a string or enum value."));
            return;
        }

        if (option.Choices is null || option.Choices.Count == 0)
            return;

        if (option.Choices.Any(choice => choice is not null && string.Equals(choice.Value, defaultValue, StringComparison.Ordinal)))
            return;

        errors.Add(new DesignerMetadataValidationError(
            defaultValuePath,
            "Default value for enum options must match one of the option choices."));
    }

    private static bool IsNumericDefaultValue(object value)
        => value is byte or sbyte
            or short or ushort
            or int or uint
            or long or ulong
            or float or double or decimal;

    private static void ValidateChoiceUsage(
        OptionDesignMetadata option,
        string optionPath,
        ICollection<DesignerMetadataValidationError> errors)
    {
        var choicesPath = $"{optionPath}.{nameof(OptionDesignMetadata.Choices)}";

        if (option.Kind == OptionValueKind.Enum)
        {
            if (option.Choices.Count == 0)
            {
                errors.Add(new DesignerMetadataValidationError(
                    choicesPath,
                    "Enum options must define at least one choice."));
            }

            return;
        }

        if (option.Choices.Count > 0)
        {
            errors.Add(new DesignerMetadataValidationError(
                choicesPath,
                "Only enum options can define choices."));
        }
    }

    private static void ValidateChoices(
        IReadOnlyList<OptionChoiceMetadata>? choices,
        string optionPath,
        ICollection<DesignerMetadataValidationError> errors)
    {
        if (choices is null)
        {
            errors.Add(new DesignerMetadataValidationError(
                $"{optionPath}.{nameof(OptionDesignMetadata.Choices)}",
                "Choices collection is required."));
            return;
        }

        var values = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index];
            var path = $"{optionPath}.{nameof(OptionDesignMetadata.Choices)}[{index}]";

            if (choice is null)
            {
                errors.Add(new DesignerMetadataValidationError(path, "Choice metadata is required."));
                continue;
            }

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

    private static void ValidateResources(
        IReadOnlyList<ResourceDesignMetadata>? resources,
        ICollection<DesignerMetadataValidationError> errors)
    {
        if (resources is null)
        {
            errors.Add(new DesignerMetadataValidationError(
                nameof(ComponentDesignMetadata.Resources),
                "Resources collection is required."));
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < resources.Count; index++)
        {
            var resource = resources[index];
            var path = $"{nameof(ComponentDesignMetadata.Resources)}[{index}]";

            if (resource is null)
            {
                errors.Add(new DesignerMetadataValidationError(path, "Resource metadata is required."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(resource.Name))
            {
                errors.Add(new DesignerMetadataValidationError($"{path}.{nameof(ResourceDesignMetadata.Name)}", "Resource name is required."));
            }
            else if (!names.Add(resource.Name))
            {
                errors.Add(new DesignerMetadataValidationError($"{path}.{nameof(ResourceDesignMetadata.Name)}", $"Resource name '{resource.Name}' is already used."));
            }

            ValidateOptionalText(resource.DisplayName, $"{path}.{nameof(ResourceDesignMetadata.DisplayName)}", errors);
            ValidateOptionalText(resource.Summary, $"{path}.{nameof(ResourceDesignMetadata.Summary)}", errors);
            ValidateOptionalText(resource.ValueType, $"{path}.{nameof(ResourceDesignMetadata.ValueType)}", errors);
            ValidateAttributes(resource.Attributes, $"{path}.{nameof(ResourceDesignMetadata.Attributes)}", errors);
        }
    }

    private static void ValidatePorts(
        IReadOnlyList<PortDesignMetadata>? ports,
        ICollection<DesignerMetadataValidationError> errors)
    {
        if (ports is null)
        {
            errors.Add(new DesignerMetadataValidationError(
                nameof(ComponentDesignMetadata.Ports),
                "Ports collection is required."));
            return;
        }

        var names = new HashSet<(PortDirection Direction, string Name)>();

        for (var index = 0; index < ports.Count; index++)
        {
            var port = ports[index];
            var path = $"{nameof(ComponentDesignMetadata.Ports)}[{index}]";

            if (port is null)
            {
                errors.Add(new DesignerMetadataValidationError(path, "Port metadata is required."));
                continue;
            }

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
        IReadOnlyDictionary<string, string>? attributes,
        string path,
        ICollection<DesignerMetadataValidationError> errors)
    {
        if (attributes is null)
        {
            errors.Add(new DesignerMetadataValidationError(path, "Attributes collection is required."));
            return;
        }

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
