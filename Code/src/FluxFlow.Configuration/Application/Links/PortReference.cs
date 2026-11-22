
using FluxFlow.Application.Addressing;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace FluxFlow.Configuration.Application.Links;

/// <summary>References a source or target port, optionally with a condition for the connection.</summary>
public sealed record PortReference(PortAddress Address, string? Condition = null,
    string? Engine = null)
{
    /// <summary>Reads one address or reference object and resolves it within the component's workflow.</summary>
    public static PortReference Read(IConfigurationSection section, ComponentAddress component)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(component);
        return Read(section, component, section.Path);
    }

    internal static PortReference Read(IConfigurationSection section, ComponentAddress component, string location)
    {
        var children = section.GetChildren().ToArray();
        if (section.Value is not null && children.Length != 0)
            throw new JsonException($"{location}: a port reference cannot contain both a scalar value and child fields");
        if (children.Length == 0 && section.Value is not null)
        {
            if (string.IsNullOrWhiteSpace(section.Value))
                throw new JsonException($"{location}: port reference strings cannot be empty");
            return Create(section.Value, null, null);
        }
        if (children.Length == 0)
            throw new JsonException($"{location}: expected a port address string or an object with 'Address' and optional 'Condition' properties");
        if (children[0].Key == "0")
            throw new JsonException($"{location}: expected one link, not an array; use OneOrManyPortReferences for a collection");

        string? address = null;
        string? condition = null;
        string? engine = null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var property in children)
        {
            if (!names.Add(property.Key))
            {
                Error($"duplicate property '{property.Key}'");
                continue;
            }
            var scalar = property.GetChildren().Any() ? null : property.Value;
            switch (property.Key.ToUpperInvariant())
            {
                case "ENGINE":
                    if (!string.IsNullOrWhiteSpace(scalar)) engine = scalar;
                    else Error("'Engine' must be a non-empty string");
                    break;
                case "ADDRESS" when scalar is not null:
                    address = scalar;
                    break;
                case "CONDITION" when scalar is not null:
                    condition = scalar;
                    break;
                case "ADDRESS":
                    Error("'Address' must be a string");
                    break;
                case "CONDITION":
                    Error("'Condition' must be a string");
                    break;
                default:
                    Error($"unknown property '{property.Key}'; supported link fields are Address, Condition and Engine");
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(address)) Error("a non-empty 'Address' string is required");
        if (condition is not null && string.IsNullOrWhiteSpace(condition)) Error("'Condition' cannot be empty");
        if (engine is not null && condition is null) Error("'Engine' requires a 'Condition'");
        if (errors.Count != 0) throw new JsonException(string.Join("; ", errors));
        return Create(address!, condition, engine);

        void Error(string reason) => errors.Add($"{location}: {reason}");

        PortReference Create(string reference, string? condition, string? engine)
        {
            try { return new(PortAddress.Resolve(reference, component), condition, engine); }
            catch (Exception error) when (error is ArgumentException or FormatException)
            { throw new JsonException($"{location}: {error.Message}", error); }
        }
    }
}
