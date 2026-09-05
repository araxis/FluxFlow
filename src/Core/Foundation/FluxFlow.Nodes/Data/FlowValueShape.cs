using System.Text.Json;

namespace FluxFlow.Data;

/// <summary>
/// Discoverable structural input accepted by a <see cref="IFlowValueMaterializer{T}"/>.
/// It intentionally describes only inexpensive top-level checks; domain validation remains
/// the responsibility of the component that owns the materialized request.
/// </summary>
public sealed class FlowValueShape : IEquatable<FlowValueShape>
{
    private static readonly JsonValueKind[] AllKinds =
    [
        JsonValueKind.Object,
        JsonValueKind.Array,
        JsonValueKind.String,
        JsonValueKind.Number,
        JsonValueKind.True,
        JsonValueKind.False,
        JsonValueKind.Null
    ];

    public FlowValueShape(
        string description,
        IReadOnlyList<JsonValueKind> acceptedKinds,
        IReadOnlyList<string>? requiredProperties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(acceptedKinds);

        var kinds = acceptedKinds
            .Distinct()
            .ToArray();
        if (kinds.Length == 0)
            throw new ArgumentException("At least one accepted JSON kind is required.", nameof(acceptedKinds));
        if (kinds.Any(static kind => kind == JsonValueKind.Undefined || !Enum.IsDefined(kind)))
            throw new ArgumentException("Accepted JSON kinds cannot contain undefined values.", nameof(acceptedKinds));

        var properties = (requiredProperties ?? [])
            .Select(static property =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(property);
                return property.Trim();
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (properties.Length > 0 && !kinds.Contains(JsonValueKind.Object))
        {
            throw new ArgumentException(
                "Required properties can only be declared for an accepted object shape.",
                nameof(requiredProperties));
        }

        Description = description.Trim();
        AcceptedKinds = Array.AsReadOnly(kinds);
        RequiredProperties = Array.AsReadOnly(properties);
    }

    public string Description { get; }

    public IReadOnlyList<JsonValueKind> AcceptedKinds { get; }

    public IReadOnlyList<string> RequiredProperties { get; }

    public bool Equals(FlowValueShape? other)
        => ReferenceEquals(this, other) || other is not null &&
           Description == other.Description &&
           AcceptedKinds.Count == other.AcceptedKinds.Count &&
           AcceptedKinds.All(other.AcceptedKinds.Contains) &&
           RequiredProperties.Count == other.RequiredProperties.Count &&
           RequiredProperties.All(property => other.RequiredProperties.Contains(
               property, StringComparer.OrdinalIgnoreCase));

    public override bool Equals(object? obj) => obj is FlowValueShape other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Description, StringComparer.Ordinal);
        foreach (var kind in AcceptedKinds.Order())
            hash.Add(kind);
        foreach (var property in RequiredProperties.Order(StringComparer.OrdinalIgnoreCase))
            hash.Add(property, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }

    public FlowValueShapeValidationResult Validate(FlowValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var element = value.ToJsonElement();
        if (!AcceptedKinds.Contains(element.ValueKind))
        {
            return FlowValueShapeValidationResult.Failure(
                $"Expected {string.Join(" or ", AcceptedKinds)} but received {element.ValueKind}.");
        }

        if (element.ValueKind != JsonValueKind.Object || RequiredProperties.Count == 0)
            return FlowValueShapeValidationResult.Success;

        var available = element.EnumerateObject()
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = RequiredProperties
            .Where(property => !available.Contains(property))
            .ToArray();
        return missing.Length == 0
            ? FlowValueShapeValidationResult.Success
            : FlowValueShapeValidationResult.Failure(
                $"Missing required properties: {string.Join(", ", missing)}.");
    }

    public static FlowValueShape Any(string description)
        => new(description, AllKinds);

    public static FlowValueShape String(string description)
        => new(description, [JsonValueKind.String]);

    public static FlowValueShape Object(
        string description,
        params string[] requiredProperties)
        => new(description, [JsonValueKind.Object], requiredProperties);
}

public sealed record FlowValueShapeValidationResult
{
    private FlowValueShapeValidationResult(bool isValid, string? error)
    {
        IsValid = isValid;
        Error = error;
    }

    public bool IsValid { get; }

    public string? Error { get; }

    public static FlowValueShapeValidationResult Success { get; } = new(true, null);

    public static FlowValueShapeValidationResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new FlowValueShapeValidationResult(false, error.Trim());
    }
}
