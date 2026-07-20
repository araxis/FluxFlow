using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.DesignerHost;

/// <summary>
/// Projects a validated <see cref="ComponentDesignMetadataCatalog"/> into the
/// host-local view models. This is the single place Designer contract types are
/// read; everything downstream (palette, inspector, pickers, renderer) works on
/// plain host models. The projection is pure: no reflection, no discovery, no
/// resource access.
/// </summary>
public sealed class DesignerHostCatalog
{
    public const string DefaultCategory = "General";
    public const string DefaultSection = "General";

    private readonly ComponentDesignMetadataCatalog _catalog;

    public DesignerHostCatalog(ComponentDesignMetadataCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>Palette items ordered by category, then display name, then type.</summary>
    public IReadOnlyList<PaletteItemModel> CreatePaletteItems()
        => _catalog.All
            .Select(CreatePaletteItem)
            .OrderBy(item => item.Category, StringComparer.Ordinal)
            .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(item => item.ComponentType, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Inspector model for one component type, or null when unknown.</summary>
    public NodeInspectorModel? CreateInspector(string componentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        if (!_catalog.TryGet(new ComponentType(componentType.Trim()), out var metadata))
            return null;

        return new NodeInspectorModel
        {
            ComponentType = metadata.Type.Value,
            Sections = CreateSections(metadata),
            ResourcePrompts = CreateResourcePrompts(metadata)
        };
    }

    /// <summary>Resource picker prompts for one component type; empty when unknown.</summary>
    public IReadOnlyList<ResourcePickerPromptModel> CreateResourcePrompts(string componentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        return _catalog.TryGet(new ComponentType(componentType.Trim()), out var metadata)
            ? CreateResourcePrompts(metadata)
            : [];
    }

    private static PaletteItemModel CreatePaletteItem(ComponentDesignMetadata metadata)
        => new()
        {
            ComponentType = metadata.Type.Value,
            DisplayName = metadata.DisplayName?.Value ?? metadata.Type.Value,
            Category = metadata.Category?.Value ?? DefaultCategory,
            Summary = metadata.Summary?.Value,
            IconKey = metadata.IconKey?.Value,
            PreferredNodeName = metadata.PreferredNodeName?.Value,
            Inputs = CreatePorts(
                metadata,
                PortDirection.Input,
                PortKind.Input,
                includeSignals: false),
            SignalInputs = CreatePorts(
                metadata,
                PortDirection.Input,
                PortKind.SignalInput,
                includeSignals: true),
            Outputs = CreatePorts(metadata, PortDirection.Output, PortKind.Output)
        };

    private static IReadOnlyList<PortModel> CreatePorts(
        ComponentDesignMetadata metadata,
        PortDirection direction,
        PortKind kind,
        bool? includeSignals = null)
        => metadata.Ports
            .Where(port =>
                port.Direction == direction &&
                (includeSignals is null || IsSignal(port) == includeSignals))
            .OrderBy(port => port.Order)
            .ThenBy(port => port.Name.Value, StringComparer.Ordinal)
            .Select(port => new PortModel
            {
                Name = port.Name.Value,
                Kind = kind,
                DisplayName = port.DisplayName?.Value,
                Group = port.Group?.Value,
                Order = port.Order,
                Summary = port.Summary?.Value,
                ValueType = port.ValueType?.Value,
                IsPrimary = port.IsPrimary
            })
            .ToArray();

    private static bool IsSignal(PortDesignMetadata port)
        => port.Attributes.TryGetValue(
               new ComponentAttributeName(PortDesignMetadataAttributeNames.Kind),
               out var kind) &&
           string.Equals(
               kind.Value,
               PortDesignMetadataAttributeValues.Signal,
               StringComparison.Ordinal);

    private static IReadOnlyList<OptionSectionModel> CreateSections(ComponentDesignMetadata metadata)
    {
        // Sections keep first-appearance order from the metadata; within a section
        // primary options render before advanced ones, each group in metadata order.
        var sectionOrder = new List<string>();
        var primaryBySection = new Dictionary<string, List<OptionEditorModel>>(StringComparer.Ordinal);
        var advancedBySection = new Dictionary<string, List<OptionEditorModel>>(StringComparer.Ordinal);

        foreach (var option in metadata.Options)
        {
            var section = GetAttribute(option.Attributes, OptionDesignMetadataAttributeNames.Section)
                ?? DefaultSection;
            if (!primaryBySection.ContainsKey(section))
            {
                sectionOrder.Add(section);
                primaryBySection.Add(section, []);
                advancedBySection.Add(section, []);
            }

            var model = CreateOptionEditor(option);
            (model.IsAdvanced ? advancedBySection : primaryBySection)[section].Add(model);
        }

        return sectionOrder
            .Select(section => new OptionSectionModel
            {
                Name = section,
                Options = [.. primaryBySection[section], .. advancedBySection[section]]
            })
            .ToArray();
    }

    private static OptionEditorModel CreateOptionEditor(OptionDesignMetadata option)
    {
        var importance = GetAttribute(option.Attributes, OptionDesignMetadataAttributeNames.Importance);
        return new OptionEditorModel
        {
            Name = option.Name.Value,
            DisplayName = option.DisplayName?.Value ?? option.Name.Value,
            Editor = ResolveEditor(option),
            HelperText = option.HelperText?.Value,
            Syntax = GetAttribute(option.Attributes, OptionDesignMetadataAttributeNames.Syntax),
            IsRequired = option.IsRequired,
            IsAdvanced = string.Equals(
                importance, OptionDesignMetadataAttributeValues.Advanced, StringComparison.Ordinal),
            DefaultValue = option.DefaultValue,
            Min = option.Min,
            Max = option.Max,
            Choices = option.Choices
                .Select(choice => new OptionChoiceModel
                {
                    Value = choice.Value.Value,
                    DisplayName = choice.DisplayName?.Value ?? choice.Value.Value,
                    HelperText = choice.HelperText?.Value
                })
                .ToArray(),
            RelatedResource = GetAttribute(
                option.Attributes, OptionDesignMetadataAttributeNames.RelatedResource)
        };
    }

    private static OptionEditorKind ResolveEditor(OptionDesignMetadata option)
    {
        // A contract-valued editor hint wins when this host has a matching editor.
        var editor = GetAttribute(option.Attributes, OptionDesignMetadataAttributeNames.Editor);
        switch (editor)
        {
            case OptionDesignMetadataAttributeValues.Text:
                return OptionEditorKind.Text;
            case OptionDesignMetadataAttributeValues.Number:
                return OptionEditorKind.Number;
            case OptionDesignMetadataAttributeValues.Expression:
                return OptionEditorKind.Expression;
            case OptionDesignMetadataAttributeValues.Json:
                return OptionEditorKind.Json;
        }

        // Unknown or missing editor hints fall back to the option value kind.
        return option.Kind switch
        {
            OptionValueKind.Text => OptionEditorKind.Text,
            OptionValueKind.Number => OptionEditorKind.Number,
            OptionValueKind.Boolean => OptionEditorKind.Toggle,
            OptionValueKind.Enum => OptionEditorKind.Select,
            OptionValueKind.MultilineText => OptionEditorKind.MultilineText,
            OptionValueKind.Json => OptionEditorKind.Json,
            OptionValueKind.Expression => OptionEditorKind.Expression,
            OptionValueKind.Duration => OptionEditorKind.Duration,
            OptionValueKind.Secret => OptionEditorKind.Secret,
            _ => OptionEditorKind.Text
        };
    }

    private static IReadOnlyList<ResourcePickerPromptModel> CreateResourcePrompts(
        ComponentDesignMetadata metadata)
        => ComponentResourcePickerHints.Create(metadata)
            .Select(hint => new ResourcePickerPromptModel
            {
                ResourceName = hint.ResourceName.Value,
                DisplayName = hint.DisplayName?.Value ?? hint.ResourceName.Value,
                PickerKind = hint.PickerKind,
                Summary = hint.Summary?.Value,
                KeyPattern = hint.KeyPattern,
                ValueType = hint.ValueType?.Value,
                IsRequired = hint.IsRequired,
                RelatedOption = hint.RelatedOption?.Value,
                RequiredWhenAnyOptions = hint.RequiredWhenAnyOptions
                    .Select(option => option.Value)
                    .ToArray()
            })
            .ToArray();

    private static string? GetAttribute(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes.TryGetValue(new ComponentAttributeName(name), out var value)
            ? value.Value
            : null;
}
