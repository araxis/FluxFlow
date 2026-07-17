using System.Collections.Immutable;
using System.Text.Json;

namespace FluxFlow.Composition.Model;

internal static class DefinitionRules
{
    private static readonly ImmutableDictionary<string, JsonElement> EmptyProperties =
        ImmutableDictionary.Create<string, JsonElement>(StringComparer.Ordinal);

    public static string RequireType(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Definition type cannot be empty.", parameterName);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Definition type cannot have surrounding whitespace.", parameterName);

        return value;
    }

    public static string RequireSegment(
        string? value,
        string parameterName,
        string role)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{role} cannot be empty.", parameterName);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException($"{role} cannot have surrounding whitespace.", parameterName);
        if (value.Contains('.'))
            throw new ArgumentException($"{role} cannot contain '.'.", parameterName);

        return value;
    }

    public static string RequireWorkflowName(string? value, string parameterName)
    {
        var name = RequireSegment(value, parameterName, "Workflow name");
        if (string.Equals(name, "Resources", StringComparison.Ordinal) ||
            string.Equals(name, "System", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Workflow name '{name}' is reserved by the application address space.",
                parameterName);
        }

        return name;
    }

    public static string RequireResourceName(string? value, string parameterName)
    {
        var name = RequireSegment(value, parameterName, "Resource name");
        if (string.Equals(name, "Type", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Resource name 'Type' is reserved as the resource-instance discriminator.",
                parameterName);
        }

        return name;
    }

    public static ImmutableDictionary<string, TValue> CopyNamed<TValue>(
        IEnumerable<KeyValuePair<string, TValue>>? source,
        string collectionName,
        Func<string?, string, string> validateName)
        where TValue : class
    {
        var builder = ImmutableDictionary.CreateBuilder<string, TValue>(StringComparer.Ordinal);
        if (source is null)
            return builder.ToImmutable();

        foreach (var (rawName, value) in source)
        {
            var name = validateName(rawName, collectionName);
            if (value is null)
                throw new ArgumentException($"{collectionName} entry '{name}' cannot be null.", collectionName);
            if (!builder.TryAdd(name, value))
                throw new ArgumentException($"{collectionName} contains duplicate name '{name}'.", collectionName);
        }

        return builder.ToImmutable();
    }

    public static ImmutableDictionary<string, JsonElement> CopyProperties(
        IEnumerable<KeyValuePair<string, JsonElement>>? source,
        string collectionName,
        bool rejectLegacyComponentWrappers)
    {
        if (source is null)
            return EmptyProperties;

        var builder = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (rawName, value) in source)
        {
            var name = RequireSegment(rawName, collectionName, "Property name");
            if (string.Equals(name, "Type", StringComparison.Ordinal))
                throw new ArgumentException("Property name 'Type' is reserved.", collectionName);
            if (rejectLegacyComponentWrappers &&
                (string.Equals(name, "Configuration", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "Resources", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"Component property '{name}' is a legacy wrapper; component settings must be flat.",
                    collectionName);
            }
            if (value.ValueKind == JsonValueKind.Undefined)
                throw new ArgumentException($"Property '{name}' cannot be undefined.", collectionName);
            ValidateJsonValue(value, $"{collectionName}.{name}", collectionName);
            if (!builder.TryAdd(name, value.Clone()))
                throw new ArgumentException($"{collectionName} contains duplicate name '{name}'.", collectionName);
        }

        return builder.Count == 0 ? EmptyProperties : builder.ToImmutable();
    }

    private static void ValidateJsonValue(
        JsonElement value,
        string path,
        string parameterName)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new ArgumentException(
                        $"JSON value '{path}' contains duplicate property '{property.Name}'.",
                        parameterName);
                }

                ValidateJsonValue(property.Value, $"{path}.{property.Name}", parameterName);
            }

            return;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ValidateJsonValue(item, $"{path}[{index}]", parameterName);
                index++;
            }
        }
    }
}
