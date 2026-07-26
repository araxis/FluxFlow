using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Testing;

public static class ComponentDesignMetadataAssertions
{
    public static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

    public static Dictionary<string, ResourceDesignMetadata> ResourcesByName(
        ComponentDesignMetadata metadata)
        => metadata.Resources.ToDictionary(
            resource => resource.Name.Value,
            StringComparer.Ordinal);

    public static void AssertOption(
        ComponentDesignMetadata metadata,
        string optionName,
        OptionValueKind kind,
        object? defaultValue = null,
        double? min = null,
        bool? isRequired = null)
    {
        var option = metadata.Options.Single(option => option.Name.Value == optionName);
        Ensure(option.Kind == kind, $"Option '{optionName}' kind was '{option.Kind}', expected '{kind}'.");

        if (defaultValue is not null)
        {
            Ensure(
                Equals(option.DefaultValue, defaultValue),
                $"Option '{optionName}' default was '{option.DefaultValue}', expected '{defaultValue}'.");
        }

        if (min.HasValue)
        {
            Ensure(option.Min == min, $"Option '{optionName}' minimum was '{option.Min}', expected '{min}'.");
        }

        if (isRequired.HasValue)
        {
            Ensure(
                option.IsRequired == isRequired.Value,
                $"Option '{optionName}' required flag was '{option.IsRequired}', expected '{isRequired.Value}'.");
        }
    }

    public static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null,
        string? syntax = null,
        string? relatedResource = null)
    {
        Ensure(
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section) == section,
            $"Option '{option.Name.Value}' section did not match '{section}'.");
        Ensure(
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance) == importance,
            $"Option '{option.Name.Value}' importance did not match '{importance}'.");
        AssertOptionalAttribute(
            option.Attributes,
            OptionDesignMetadataAttributeNames.Editor,
            editor);
        AssertOptionalAttribute(
            option.Attributes,
            OptionDesignMetadataAttributeNames.Syntax,
            syntax);
        AssertOptionalAttribute(
            option.Attributes,
            OptionDesignMetadataAttributeNames.RelatedResource,
            relatedResource);
    }

    public static void AssertResourceHints(
        ResourceDesignMetadata resource,
        string pickerKind,
        string keyPattern)
    {
        Ensure(
            AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership) ==
            ResourceDesignMetadataAttributeValues.HostOwned,
            $"Resource '{resource.Name.Value}' is not host-owned.");
        Ensure(
            AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind) == pickerKind,
            $"Resource '{resource.Name.Value}' picker kind did not match '{pickerKind}'.");
        Ensure(
            AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern) == keyPattern,
            $"Resource '{resource.Name.Value}' key pattern did not match '{keyPattern}'.");
    }

    public static void AssertPorts(
        ComponentDesignMetadata metadata,
        IReadOnlyList<(string Name, PortDirection Direction, int Order, bool IsPrimary, string ValueType)> expected)
    {
        var actual = metadata.Ports.Select(port => (
            port.Name.Value,
            port.Direction,
            port.Order,
            port.IsPrimary,
            port.ValueType?.Value!));
        Ensure(actual.SequenceEqual(expected), $"Ports for '{metadata.Type.Value}' did not match.");
    }

    public static void AssertResources(
        ComponentDesignMetadata metadata,
        IReadOnlyList<(string Name, int Order, bool IsRequired, string ValueType)> expected)
    {
        var actual = metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value!));
        Ensure(actual.SequenceEqual(expected), $"Resources for '{metadata.Type.Value}' did not match.");
    }

    public static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static void AssertOptionalAttribute(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name,
        string? expected)
    {
        var key = new ComponentAttributeName(name);
        if (expected is null)
        {
            Ensure(!attributes.ContainsKey(key), $"Attribute '{name}' was not expected.");
            return;
        }

        Ensure(attributes[key].Value == expected, $"Attribute '{name}' did not match '{expected}'.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
