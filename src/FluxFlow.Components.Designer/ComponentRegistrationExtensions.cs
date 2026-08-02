using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace FluxFlow.Components.Designer;

public static class ComponentRegistrationExtensions
{
    public static FluxFlowRegistrationBuilder AddComponent(
        this FluxFlowRegistrationBuilder builder,
        string type,
        Action<ComponentRegistrationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(configure);

        var component = new ComponentRegistrationBuilder(type);
        configure(component);
        var metadata = component.CreateMetadata();

        var existingDeclaration = GetDeclarations(builder.Services)
            .SingleOrDefault(declaration => string.Equals(
                declaration.Descriptor.Type,
                metadata.Type.Value,
                StringComparison.Ordinal));
        if (existingDeclaration is not null &&
            !ComponentDesignMetadataEquality.Equals(existingDeclaration.Metadata, metadata))
        {
            throw new InvalidOperationException(
                $"Component type '{metadata.Type}' has a conflicting design registration.");
        }

        builder.AddRuntimeComponent(type, component.CopyRuntimeTo);
        var descriptor = builder.Services
            .Where(static registration => registration.ServiceType == typeof(ComponentDescriptor))
            .Select(static registration => registration.ImplementationInstance)
            .OfType<ComponentDescriptor>()
            .Single(candidate => string.Equals(
                candidate.Type,
                metadata.Type.Value,
                StringComparison.Ordinal));

        if (existingDeclaration is null)
            builder.Services.AddSingleton(new ComponentDesignDeclaration(descriptor, metadata));

        EnsureDesignMetadataCatalog(builder.Services);

        return builder;
    }

    private static void EnsureDesignMetadataCatalog(IServiceCollection services)
        => services.TryAddSingleton(static provider =>
            ComponentDesignMetadataCatalog.FromDeclarations(
                provider.GetServices<ComponentDesignDeclaration>()));

    private static ComponentDesignDeclaration[] GetDeclarations(IServiceCollection services)
        => services
            .Where(static registration => registration.ServiceType == typeof(ComponentDesignDeclaration))
            .Select(static registration => registration.ImplementationInstance)
            .OfType<ComponentDesignDeclaration>()
            .ToArray();

    private static class ComponentDesignMetadataEquality
    {
        public static bool Equals(ComponentDesignMetadata left, ComponentDesignMetadata right)
            => left.Type == right.Type &&
               left.DisplayName == right.DisplayName &&
               left.Category == right.Category &&
               left.Summary == right.Summary &&
               left.IconKey == right.IconKey &&
               left.PreferredNodeName == right.PreferredNodeName &&
               left.SuggestedEditorWidth == right.SuggestedEditorWidth &&
               left.ProcessingCapabilities == right.ProcessingCapabilities &&
               left.Options.SequenceEqual(right.Options, OptionComparer.Instance) &&
               left.Resources.SequenceEqual(right.Resources, ResourceComparer.Instance) &&
               left.Ports.SequenceEqual(right.Ports, PortComparer.Instance) &&
               DictionariesEqual(left.Attributes, right.Attributes);

        private static bool DictionariesEqual<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue> left,
            IReadOnlyDictionary<TKey, TValue> right)
            where TKey : notnull
        {
            if (left.Count != right.Count)
                return false;

            foreach (var item in left)
            {
                if (!right.TryGetValue(item.Key, out var value) ||
                    !EqualityComparer<TValue>.Default.Equals(item.Value, value))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class OptionComparer : IEqualityComparer<OptionDesignMetadata>
        {
            public static OptionComparer Instance { get; } = new();

            public bool Equals(OptionDesignMetadata? left, OptionDesignMetadata? right)
                => ReferenceEquals(left, right) ||
                   left is not null && right is not null &&
                   left.Name == right.Name &&
                   left.Kind == right.Kind &&
                   left.DisplayName == right.DisplayName &&
                   left.HelperText == right.HelperText &&
                   left.IsRequired == right.IsRequired &&
                   DefaultValuesEqual(left.DefaultValue, right.DefaultValue) &&
                   left.Min == right.Min &&
                   left.Max == right.Max &&
                   left.Choices.SequenceEqual(right.Choices, ChoiceComparer.Instance) &&
                   DictionariesEqual(left.Attributes, right.Attributes);

            public int GetHashCode(OptionDesignMetadata obj) => obj.Name.GetHashCode();
        }

        private static bool DefaultValuesEqual(object? left, object? right)
        {
            if (object.Equals(left, right))
                return true;
            if (left is null || right is null || left.GetType() != right.GetType())
                return false;

            try
            {
                var leftJson = JsonSerializer.SerializeToElement(left, left.GetType());
                var rightJson = JsonSerializer.SerializeToElement(right, right.GetType());
                return JsonElementsEqual(leftJson, rightJson);
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        private static bool JsonElementsEqual(JsonElement left, JsonElement right)
        {
            if (left.ValueKind != right.ValueKind)
                return false;

            return left.ValueKind switch
            {
                JsonValueKind.Object => JsonObjectsEqual(left, right),
                JsonValueKind.Array => JsonArraysEqual(left, right),
                JsonValueKind.String => left.GetString() == right.GetString(),
                JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
                JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined => true,
                _ => false
            };
        }

        private static bool JsonObjectsEqual(JsonElement left, JsonElement right)
        {
            var leftProperties = left.EnumerateObject().ToArray();
            var rightProperties = right.EnumerateObject().ToArray();
            if (leftProperties.Length != rightProperties.Length)
                return false;

            return leftProperties.All(property =>
                right.TryGetProperty(property.Name, out var rightValue) &&
                JsonElementsEqual(property.Value, rightValue));
        }

        private static bool JsonArraysEqual(JsonElement left, JsonElement right)
        {
            var leftItems = left.EnumerateArray().ToArray();
            var rightItems = right.EnumerateArray().ToArray();
            return leftItems.Length == rightItems.Length &&
                   leftItems.Zip(rightItems).All(pair => JsonElementsEqual(pair.First, pair.Second));
        }

        private sealed class ChoiceComparer : IEqualityComparer<OptionChoiceMetadata>
        {
            public static ChoiceComparer Instance { get; } = new();

            public bool Equals(OptionChoiceMetadata? left, OptionChoiceMetadata? right)
                => ReferenceEquals(left, right) ||
                   left is not null && right is not null &&
                   left.Value == right.Value &&
                   left.DisplayName == right.DisplayName &&
                   left.HelperText == right.HelperText &&
                   DictionariesEqual(left.Attributes, right.Attributes);

            public int GetHashCode(OptionChoiceMetadata obj) => obj.Value.GetHashCode();
        }

        private sealed class ResourceComparer : IEqualityComparer<ResourceDesignMetadata>
        {
            public static ResourceComparer Instance { get; } = new();

            public bool Equals(ResourceDesignMetadata? left, ResourceDesignMetadata? right)
                => ReferenceEquals(left, right) ||
                   left is not null && right is not null &&
                   left.Name == right.Name &&
                   left.DisplayName == right.DisplayName &&
                   left.Order == right.Order &&
                   left.Summary == right.Summary &&
                   left.ValueType == right.ValueType &&
                   left.IsRequired == right.IsRequired &&
                   DictionariesEqual(left.Attributes, right.Attributes);

            public int GetHashCode(ResourceDesignMetadata obj) => obj.Name.GetHashCode();
        }

        private sealed class PortComparer : IEqualityComparer<PortDesignMetadata>
        {
            public static PortComparer Instance { get; } = new();

            public bool Equals(PortDesignMetadata? left, PortDesignMetadata? right)
                => ReferenceEquals(left, right) ||
                   left is not null && right is not null &&
                   left.Name == right.Name &&
                   left.Direction == right.Direction &&
                   left.DisplayName == right.DisplayName &&
                   left.Group == right.Group &&
                   left.Order == right.Order &&
                   left.Summary == right.Summary &&
                   left.ValueType == right.ValueType &&
                   left.MessageType == right.MessageType &&
                   left.Kind == right.Kind &&
                   left.LinkCardinality == right.LinkCardinality &&
                   left.IsPrimary == right.IsPrimary &&
                   DictionariesEqual(left.Attributes, right.Attributes);

            public int GetHashCode(PortDesignMetadata obj) => HashCode.Combine(obj.Name, obj.Direction);
        }
    }
}
