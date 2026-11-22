using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Components.RabbitMQ;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Configuration.Components.RabbitMQ;

public static class RabbitTopologyConfiguration
{
    // IConfiguration stores scalar values as strings. Explicit AMQP field types
    // restore the wire type without guessing whether a numeric-looking value is text.
    internal static RabbitTopology Read(IConfigurationSection section)
    {
        Object(section, "Exchanges", "Queues", "Bindings");
        var result = new RabbitTopology
        {
            Exchanges = Items(section.GetSection("Exchanges")).Select(item =>
            {
                Object(item, "Name", "Type", "Durable", "AutoDelete", "Arguments");
                return new RabbitExchange(item["Name"] ?? "", item["Type"] ?? "direct", Boolean(item, "Durable", true),
                    Boolean(item, "AutoDelete"), Arguments(item.GetSection("Arguments")));
            }).ToList(),
            Queues = Items(section.GetSection("Queues")).Select(item =>
            {
                Object(item, "Name", "Durable", "Exclusive", "AutoDelete", "Arguments");
                return new RabbitQueue(item["Name"] ?? "", Boolean(item, "Durable", true), Boolean(item, "Exclusive"),
                    Boolean(item, "AutoDelete"), Arguments(item.GetSection("Arguments")));
            }).ToList(),
            Bindings = Items(section.GetSection("Bindings")).Select(item =>
            {
                Object(item, "Queue", "Exchange", "RoutingKey", "Arguments");
                return new RabbitBinding(item["Queue"] ?? "", item["Exchange"] ?? "", item["RoutingKey"] ?? "", Arguments(item.GetSection("Arguments")));
            }).ToList()
        };
        result.Validate();
        return result;
    }
    public static Dictionary<string, AmqpField> ReadArguments(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
        return configuration.GetChildren().ToDictionary(item => item.Key, Field);
    }

    private static Dictionary<string, AmqpField> Arguments(IConfigurationSection section)
    {
        Object(section);
        return section.GetChildren().ToDictionary(item => item.Key, Field);
    }
    public static void ValidateArgument(IConfigurationSection section) => Field(section).Decode(static seconds => seconds);

    private static AmqpField Field(IConfigurationSection section)
    {
        Object(section, "Type", "Value");
        var type = section["Type"] ?? throw new ArgumentException($"{section.Path} needs an AMQP field Type.");
        var value = section.GetSection("Value");
        if (type is not ("Null" or "Array" or "Table") && value.Value is null)
            throw new ArgumentException($"{value.Path} requires a scalar value.");
        if (type == "Null" && value.Exists())
            throw new ArgumentException($"{value.Path} must be null for an AMQP Null field.");
        JsonNode? node = type switch
        {
            "Null" => null,
            "Array" => new JsonArray(Items(value).Select(item => JsonSerializer.SerializeToNode(Field(item))).ToArray()),
            "Table" => JsonSerializer.SerializeToNode(Arguments(value)),
            "Boolean" => JsonValue.Create(bool.Parse(value.Value!)),
            "Byte" or "SByte" or "Int16" or "UInt16" or "Int32" or "UInt32" => JsonValue.Create(long.Parse(value.Value!, System.Globalization.CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(value.Value ?? throw new ArgumentException($"{value.Path} requires a scalar value."))
        };
        return new AmqpField(type, JsonSerializer.SerializeToElement(node));
    }
    private static IConfigurationSection[] Items(IConfigurationSection section)
    {
        // The JSON configuration provider represents an empty array as an empty scalar.
        if (section.Value == "" && !section.GetChildren().Any()) return [];
        Object(section);
        var items = section.GetChildren().ToArray();
        if (items.Where((item, index) => item.Key != index.ToString(System.Globalization.CultureInfo.InvariantCulture)).Any())
            throw new ArgumentException($"{section.Path} must be an array.");
        return items;
    }
    private static bool Boolean(IConfigurationSection section, string name, bool fallback = false)
        => section[name] is { } value ? bool.Parse(value) : fallback;
    private static void Object(IConfigurationSection section, params string[] allowed)
    {
        if (section.Value is not null) throw new ArgumentException($"{section.Path} must contain structured configuration.");
        if (allowed.Length > 0 && section.GetChildren().Any(item => !allowed.Contains(item.Key, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException($"Unsupported property in {section.Path}.");
    }
}
