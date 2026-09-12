using System.Text.Json;
using FluxFlow.Core;
using FluxFlow.Json.Model;

namespace FluxFlow.Json.Links;

internal static class ApplicationLinkDeclarationParser
{
    public static ApplicationLinkDeclarationParseResult Parse(JsonElement value, string location)
    {
        var declarations = new List<ParsedApplicationLinkDeclaration>();
        var errors = new List<ApplicationLinkDeclarationParseError>();
        if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ParseOne(item, $"{location}[{index}]", declarations, errors);
                index++;
            }
        }
        else
        {
            ParseOne(value, location, declarations, errors);
        }

        return new ApplicationLinkDeclarationParseResult(declarations, errors);
    }

    internal static JsonElement Serialize(IReadOnlyList<ParsedApplicationLinkDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        if (declarations.Count == 0)
            throw new ArgumentException("At least one link declaration is required.", nameof(declarations));

        var values = declarations.Select(SerializeOne).ToArray();
        return values.Length == 1
            ? values[0]
            : JsonSerializer.SerializeToElement(values);
    }

    private static void ParseOne(
        JsonElement value,
        string location,
        List<ParsedApplicationLinkDeclaration> declarations,
        List<ApplicationLinkDeclarationParseError> errors)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var portReference = value.GetString();
            if (!string.IsNullOrWhiteSpace(portReference))
            {
                declarations.Add(new ParsedApplicationLinkDeclaration(portReference, null));
                return;
            }

            errors.Add(new(location, "port reference strings cannot be empty"));
            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new(
                location,
                "expected a port string or an object with exact 'Port' and optional 'Condition' properties"));
            return;
        }

        string? port = null;
        string? condition = null;
        var delivery = FlowDeliveryPolicy.Reliable;
        var fallback = false;
        IReadOnlyDictionary<string, JsonElement>? parameters = null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var valid = true;
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                errors.Add(new(location, $"duplicate property '{property.Name}'"));
                valid = false;
                continue;
            }
            switch (property.Name)
            {
                case CanonicalApplicationProperties.LinkFallback:
                    if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        fallback = property.Value.GetBoolean();
                    else
                    {
                        errors.Add(new(location, "'Fallback' must be a boolean"));
                        valid = false;
                    }
                    break;
                case CanonicalApplicationProperties.LinkDelivery:
                    if (property.Value.ValueKind != JsonValueKind.String ||
                        !Enum.TryParse(property.Value.GetString(), ignoreCase: false, out delivery) ||
                        !Enum.IsDefined(delivery) ||
                        property.Value.GetString() != delivery.ToString())
                    {
                        errors.Add(new(location, "'Delivery' must be an exact named delivery policy"));
                        valid = false;
                    }
                    break;
                case CanonicalApplicationProperties.LinkParameters when property.Value.ValueKind == JsonValueKind.Object:
                    try
                    {
                        parameters = ConditionParameters.Copy(property.Value.EnumerateObject()
                            .Select(static item => new KeyValuePair<string, JsonElement>(item.Name, item.Value)));
                    }
                    catch (ArgumentException exception)
                    {
                        errors.Add(new(location, exception.Message));
                        valid = false;
                    }
                    break;
                case CanonicalApplicationProperties.LinkParameters:
                    errors.Add(new(location, "'Parameters' must be an object"));
                    valid = false;
                    break;
                case CanonicalApplicationProperties.LinkPort when property.Value.ValueKind == JsonValueKind.String:
                    port = property.Value.GetString();
                    break;
                case CanonicalApplicationProperties.LinkCondition when property.Value.ValueKind == JsonValueKind.String:
                    condition = property.Value.GetString();
                    break;
                case CanonicalApplicationProperties.LinkPort:
                    errors.Add(new(location, "'Port' must be a string"));
                    valid = false;
                    break;
                case CanonicalApplicationProperties.LinkCondition:
                    errors.Add(new(location, "'Condition' must be a string"));
                    valid = false;
                    break;
                default:
                    errors.Add(new(
                        location,
                        $"unknown property '{property.Name}'; link object property names are case-sensitive"));
                    valid = false;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(port))
        {
            errors.Add(new(location, "a non-empty 'Port' string is required"));
            valid = false;
        }

        if (condition is not null && string.IsNullOrWhiteSpace(condition))
        {
            errors.Add(new(location, "'Condition' cannot be empty"));
            valid = false;
        }

        if (parameters is not null && condition is null)
        {
            errors.Add(new(location, "'Parameters' requires a 'Condition'"));
            valid = false;
        }
        if (fallback && (condition is not null || parameters is not null))
        {
            errors.Add(new(location, "'Fallback' cannot have a 'Condition' or 'Parameters'"));
            valid = false;
        }
        if (valid)
            declarations.Add(new ParsedApplicationLinkDeclaration(port!, condition, parameters, delivery, fallback));
    }

    private static JsonElement SerializeOne(ParsedApplicationLinkDeclaration declaration)
        => declaration.Condition is null && declaration.Delivery == FlowDeliveryPolicy.Reliable && !declaration.IsFallback
            ? JsonSerializer.SerializeToElement(declaration.Port)
            : JsonSerializer.SerializeToElement(new LinkObject(
                declaration.Port,
                declaration.Condition,
                declaration.Parameters?.Count > 0 ? declaration.Parameters : null,
                declaration.Delivery == FlowDeliveryPolicy.Reliable ? null : declaration.Delivery.ToString(),
                declaration.IsFallback ? true : null));

    private sealed record LinkObject(string Port,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        string? Condition,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyDictionary<string, JsonElement>? Parameters,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        string? Delivery,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        bool? Fallback);
}

internal sealed record ApplicationLinkDeclarationParseResult(
    IReadOnlyList<ParsedApplicationLinkDeclaration> Declarations,
    IReadOnlyList<ApplicationLinkDeclarationParseError> Errors);

internal sealed record ParsedApplicationLinkDeclaration(string Port, string? Condition,
    IReadOnlyDictionary<string, JsonElement>? Parameters = null,
    FlowDeliveryPolicy Delivery = FlowDeliveryPolicy.Reliable, bool IsFallback = false);

internal sealed record ApplicationLinkDeclarationParseError(string Location, string Reason);
