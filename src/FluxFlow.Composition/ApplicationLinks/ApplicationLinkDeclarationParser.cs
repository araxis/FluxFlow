using System.Text.Json;
using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Links;

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
        var valid = true;
        foreach (var property in value.EnumerateObject())
        {
            switch (property.Name)
            {
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

        if (valid)
            declarations.Add(new ParsedApplicationLinkDeclaration(port!, condition));
    }

    private static JsonElement SerializeOne(ParsedApplicationLinkDeclaration declaration)
        => declaration.Condition is null
            ? JsonSerializer.SerializeToElement(declaration.Port)
            : JsonSerializer.SerializeToElement(new LinkObject(
                declaration.Port,
                declaration.Condition));

    private sealed record LinkObject(string Port, string Condition);
}

internal sealed record ApplicationLinkDeclarationParseResult(
    IReadOnlyList<ParsedApplicationLinkDeclaration> Declarations,
    IReadOnlyList<ApplicationLinkDeclarationParseError> Errors);

internal sealed record ParsedApplicationLinkDeclaration(string Port, string? Condition);

internal sealed record ApplicationLinkDeclarationParseError(string Location, string Reason);
