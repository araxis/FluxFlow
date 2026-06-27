using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public sealed class ComponentDesignMetadataBuilder
{
    private readonly ComponentType type;
    private readonly List<OptionDesignMetadata> options = [];
    private readonly List<ResourceDesignMetadata> resources = [];
    private readonly List<PortDesignMetadata> ports = [];
    private readonly Dictionary<string, string> attributes = new(StringComparer.Ordinal);
    private string? displayName;
    private ComponentCategory? category;
    private string? summary;
    private ComponentIconKey? iconKey;
    private ComponentPreferredNodeName? preferredNodeName;
    private int? suggestedEditorWidth;

    public ComponentDesignMetadataBuilder(string type)
        : this(new ComponentType(type))
    {
    }

    public ComponentDesignMetadataBuilder(ComponentType type)
    {
        this.type = type;
    }

    public ComponentDesignMetadataBuilder WithDisplay(
        string? displayName = null,
        string? category = null,
        string? summary = null,
        string? iconKey = null,
        string? preferredNodeName = null,
        int? suggestedEditorWidth = null)
    {
        this.displayName = displayName;
        this.category = category is null ? null : new ComponentCategory(category);
        this.summary = summary;
        this.iconKey = iconKey is null ? null : new ComponentIconKey(iconKey);
        this.preferredNodeName = preferredNodeName is null ? null : new ComponentPreferredNodeName(preferredNodeName);
        this.suggestedEditorWidth = suggestedEditorWidth;
        return this;
    }

    public ComponentDesignMetadataBuilder AddOption(OptionDesignMetadata option)
    {
        ArgumentNullException.ThrowIfNull(option);
        options.Add(option);
        return this;
    }

    public ComponentDesignMetadataBuilder AddOption(
        string name,
        OptionValueKind kind,
        string? displayName = null,
        string? helperText = null,
        bool isRequired = false,
        object? defaultValue = null,
        double? min = null,
        double? max = null,
        IReadOnlyList<OptionChoiceMetadata>? choices = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        var option = new OptionDesignMetadata
        {
            Name = new ComponentOptionName(name),
            Kind = kind,
            DisplayName = displayName,
            HelperText = helperText,
            IsRequired = isRequired,
            DefaultValue = defaultValue,
            Min = min,
            Max = max
        };

        if (choices is not null)
        {
            option = option with
            {
                Choices = choices
            };
        }

        if (attributes is not null)
        {
            option = option with
            {
                Attributes = attributes
            };
        }

        return AddOption(option);
    }

    public ComponentDesignMetadataBuilder AddEnumOption(
        string name,
        IEnumerable<string> choices,
        string? displayName = null,
        string? helperText = null,
        bool isRequired = false,
        string? defaultValue = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(choices);

        var optionChoices = choices.Select(choice =>
        {
            ArgumentNullException.ThrowIfNull(choice);
            return new OptionChoiceMetadata
            {
                Value = new ComponentOptionChoiceValue(choice)
            };
        }).ToArray();

        return AddOption(
            name,
            OptionValueKind.Enum,
            displayName,
            helperText,
            isRequired,
            defaultValue,
            choices: optionChoices,
            attributes: attributes);
    }

    public ComponentDesignMetadataBuilder AddResource(ResourceDesignMetadata resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resources.Add(resource);
        return this;
    }

    public ComponentDesignMetadataBuilder AddResource(
        string name,
        string? displayName = null,
        int order = 0,
        string? summary = null,
        string? valueType = null,
        bool isRequired = false,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        var resource = new ResourceDesignMetadata
        {
            Name = new ComponentResourceName(name),
            DisplayName = displayName,
            Order = order,
            Summary = summary,
            ValueType = valueType,
            IsRequired = isRequired
        };

        if (attributes is not null)
        {
            resource = resource with
            {
                Attributes = attributes
            };
        }

        return AddResource(resource);
    }

    public ComponentDesignMetadataBuilder AddPort(PortDesignMetadata port)
    {
        ArgumentNullException.ThrowIfNull(port);
        ports.Add(port);
        return this;
    }

    public ComponentDesignMetadataBuilder AddInputPort(
        string name,
        string? displayName = null,
        string? group = null,
        int order = 0,
        string? summary = null,
        string? valueType = null,
        bool isPrimary = false,
        IReadOnlyDictionary<string, string>? attributes = null)
        => AddPort(
            name,
            PortDirection.Input,
            displayName,
            group,
            order,
            summary,
            valueType,
            isPrimary,
            attributes);

    public ComponentDesignMetadataBuilder AddOutputPort(
        string name,
        string? displayName = null,
        string? group = null,
        int order = 0,
        string? summary = null,
        string? valueType = null,
        bool isPrimary = false,
        IReadOnlyDictionary<string, string>? attributes = null)
        => AddPort(
            name,
            PortDirection.Output,
            displayName,
            group,
            order,
            summary,
            valueType,
            isPrimary,
            attributes);

    public ComponentDesignMetadataBuilder AddPort(
        string name,
        PortDirection direction,
        string? displayName = null,
        string? group = null,
        int order = 0,
        string? summary = null,
        string? valueType = null,
        bool isPrimary = false,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        var port = new PortDesignMetadata
        {
            Name = new ComponentPortName(name),
            Direction = direction,
            DisplayName = displayName,
            Group = group is null ? null : new ComponentPortGroup(group),
            Order = order,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = isPrimary
        };

        if (attributes is not null)
        {
            port = port with
            {
                Attributes = attributes
            };
        }

        return AddPort(port);
    }

    public ComponentDesignMetadataBuilder AddAttributes(
        IEnumerable<KeyValuePair<string, string>> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        foreach (var attribute in attributes)
            AddAttribute(attribute.Key, attribute.Value);

        return this;
    }

    public ComponentDesignMetadataBuilder AddAttribute(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        attributes.Add(key, value);
        return this;
    }

    public ComponentDesignMetadata Build()
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = type,
            DisplayName = displayName,
            Category = category,
            Summary = summary,
            IconKey = iconKey,
            PreferredNodeName = preferredNodeName,
            SuggestedEditorWidth = suggestedEditorWidth,
            Options = options.ToArray(),
            Resources = resources.ToArray(),
            Ports = ports.ToArray(),
            Attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal)
        };

        return new ComponentDesignMetadataModule([metadata])
            .GetMetadata()
            .Single();
    }
}
