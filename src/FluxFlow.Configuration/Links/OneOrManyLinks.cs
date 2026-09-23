using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Core.Execution;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Configuration.Links;

/// <summary>One port link or a list of links, each written as an address or a link object.</summary>
public sealed class OneOrManyLinks : IReadOnlyList<LinkConfiguration>
{
    private readonly IReadOnlyList<LinkConfiguration> _links;

    private OneOrManyLinks(IReadOnlyList<LinkConfiguration> links) => _links = links;

    public static OneOrManyLinks Empty { get; } = new(Array.Empty<LinkConfiguration>());
    public int Count => _links.Count;
    public LinkConfiguration this[int index] => _links[index];
    public IEnumerator<LinkConfiguration> GetEnumerator() => _links.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static OneOrManyLinks Read(IConfigurationSection value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var location = value.Path;
        var declarations = new List<LinkConfiguration>();
        var errors = new List<(string Location, string Reason)>();
        var children = value.GetChildren().ToArray();
        if (IsArray(children))
        {
            var index = 0;
            foreach (var item in children)
            {
                ParseOne(item, $"{location}[{index}]", declarations, errors);
                index++;
            }
        }
        else
        {
            ParseOne(value, location, declarations, errors);
        }

        if (errors.Count != 0)
            throw new JsonException(string.Join("; ", errors.Select(error => $"{error.Location}: {error.Reason}")));
        return new OneOrManyLinks(declarations.AsReadOnly());
    }

    private static void ParseOne(
        IConfigurationSection value,
        string location,
        List<LinkConfiguration> declarations,
        List<(string Location, string Reason)> errors)
    {
        var children = value.GetChildren().ToArray();
        if (children.Length == 0 && value.Value is not null)
        {
            var portReference = value.Value;
            if (!string.IsNullOrWhiteSpace(portReference))
            {
                declarations.Add(new LinkConfiguration(portReference, null));
                return;
            }

            errors.Add(new(location, "port reference strings cannot be empty"));
            return;
        }

        if (children.Length == 0 || IsArray(children))
        {
            errors.Add(new(
                location,
                "expected a port string or an object with exact 'Port' and optional 'Condition' properties"));
            return;
        }

        string? port = null;
        string? condition = null;
        string? engine = null;
        var delivery = FlowDeliveryPolicy.Reliable;
        var fallback = false;
        IReadOnlyDictionary<string, JsonElement>? parameters = null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valid = true;
        foreach (var property in children)
        {
            if (!names.Add(property.Key))
            {
                errors.Add(new(location, $"duplicate property '{property.Key}'"));
                valid = false;
                continue;
            }
            var scalar = property.GetChildren().Any() ? null : property.Value;
            switch (property.Key.ToUpperInvariant())
            {
                case "ENGINE":
                    if (!string.IsNullOrWhiteSpace(scalar))
                        engine = scalar;
                    else
                    {
                        errors.Add(new(location, "'Engine' must be a non-empty string"));
                        valid = false;
                    }
                    break;
                case "FALLBACK":
                    if (bool.TryParse(property.Value, out var parsedFallback))
                        fallback = parsedFallback;
                    else
                    {
                        errors.Add(new(location, "'Fallback' must be a boolean"));
                        valid = false;
                    }
                    break;
                case "DELIVERY":
                    if (scalar is null ||
                        !Enum.TryParse(scalar, ignoreCase: true, out delivery) ||
                        !Enum.IsDefined(delivery) ||
                        !string.Equals(scalar, delivery.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(new(location, "'Delivery' must be an exact named delivery policy"));
                        valid = false;
                    }
                    break;
                case "PARAMETERS":
                    try
                    {
                        // Parameters are JSON data; preserve the existing configuration scalar rules.
                        if (ConfigurationJsonReader.Read(property) is not JsonObject parameterObject)
                            throw new ArgumentException("'Parameters' must be an object");
                        parameters = ConditionParameters.Copy(parameterObject.Select(static item =>
                            new KeyValuePair<string, JsonElement>(item.Key, JsonSerializer.SerializeToElement(item.Value))));
                    }
                    catch (ArgumentException exception)
                    {
                        errors.Add(new(location, exception.Message));
                        valid = false;
                    }
                    break;
                case "PORT" when scalar is not null:
                    port = scalar;
                    break;
                case "CONDITION" when scalar is not null:
                    condition = scalar;
                    break;
                case "PORT":
                    errors.Add(new(location, "'Port' must be a string"));
                    valid = false;
                    break;
                case "CONDITION":
                    errors.Add(new(location, "'Condition' must be a string"));
                    valid = false;
                    break;
                default:
                    errors.Add(new(
                        location,
                        $"unknown property '{property.Key}'; supported link fields are Port, Condition, Engine, Parameters, Delivery and Fallback"));
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
        if (engine is not null && condition is null)
        {
            errors.Add(new(location, "'Engine' requires a 'Condition'"));
            valid = false;
        }
        if (fallback && (condition is not null || parameters is not null || engine is not null))
        {
            errors.Add(new(location, "'Fallback' cannot have a 'Condition' or 'Parameters'"));
            valid = false;
        }
        if (valid)
            declarations.Add(new LinkConfiguration(port!, condition, parameters, delivery, fallback, engine));
    }

    private static bool IsArray(IConfigurationSection[] children)
        => children.Length != 0 && children.Select((child, index) =>
            child.Key == index.ToString(System.Globalization.CultureInfo.InvariantCulture)).All(match => match);

}
