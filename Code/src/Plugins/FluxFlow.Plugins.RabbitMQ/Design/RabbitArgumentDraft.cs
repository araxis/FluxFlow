using System.Text;
using System.Globalization;
using System.Text.Json.Nodes;
using FluxFlow.Components.RabbitMQ;
using FluxFlow.Configuration.Components.RabbitMQ;
using FluxFlow.Environments;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Plugins.RabbitMQ;

/// <summary>A detached argument draft. Unedited fields retain their original JSON representation.</summary>
public sealed class RabbitArgumentDraft : IPropertyValueDraft
{
    internal static readonly string[] Types = ["String", "Boolean", "Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "Single", "Double", "Decimal", "Timestamp", "Bytes", "Null", "Array", "Table"];
    IReadOnlyList<string> IPropertyValueDraft.Types => Types;
    public PropertyValueKind Kind => Type switch
    {
        "Array" => PropertyValueKind.List, "Table" => PropertyValueKind.Map, "Null" => PropertyValueKind.Empty,
        "Boolean" => PropertyValueKind.Toggle, "String" or "Bytes" => PropertyValueKind.Multiline, _ => PropertyValueKind.Text
    };
    public string ValueLabel => Type switch { "Bytes" => "Value (Base64)", "Timestamp" => "Value (Unix seconds)", _ => "Value" };
    public IReadOnlyList<IPropertyValueDraft> Items => Children;
    public void AddItem() => Children.Add(new(""));
    public void RemoveItem(IPropertyValueDraft item) { if (item is RabbitArgumentDraft child) Children.Remove(child); }
    private readonly JsonObject _original;
    private readonly string _originalType, _originalText;
    private readonly (RabbitArgumentDraft Draft, string Name)[] _originalChildren;
    public string Name { get; set; }
    public string Type { get; private set; }
    public string Text { get; set; }
    public List<RabbitArgumentDraft> Children { get; } = [];

    public RabbitArgumentDraft(string name, JsonObject? value = null)
    {
        Name = name;
        _original = (JsonObject)(value?.DeepClone() ?? new JsonObject { ["Type"] = "String", ["Value"] = "" });
        Type = _originalType = ResourceJson.Text(_original, "Type") ?? "";
        var stored = ResourceJson.Value(_original, "Value");
        Text = _originalText = stored is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : stored?.ToJsonString() ?? "";
        if (Type == "Array" && stored is JsonArray array)
            foreach (var item in array) Children.Add(new("", item as JsonObject ?? throw new ArgumentException("Array entries must be typed arguments.")));
        if (Type == "Table" && stored is JsonObject table)
            foreach (var item in table) Children.Add(new(item.Key, item.Value as JsonObject ?? throw new ArgumentException("Table entries must be typed arguments.")));
        if (Type == "Array" && stored is not (null or JsonArray) || Type == "Table" && stored is not (null or JsonObject))
            throw new ArgumentException("The argument collection has an invalid shape. Use Advanced JSON to repair it.");
        _originalChildren = Children.Select(child => (child, child.Name)).ToArray();
    }

    public void ChangeType(string type)
    {
        if (Type == type || !Types.Contains(type, StringComparer.Ordinal)) return;
        Type = type;
        Text = type switch { "Boolean" => "false", "String" or "Bytes" or "Null" or "Array" or "Table" => "", _ => "0" };
        Children.Clear();
    }

    public JsonObject Build()
    {
        var result = (JsonObject)_original.DeepClone();
        if (Unchanged) return result;
        if (Type != _originalType) ResourceJson.Set(result, "Type", JsonValue.Create(Type));
        if (Type == "Array")
            ResourceJson.Set(result, "Value", new JsonArray(Children.Select(child => (JsonNode)child.Build()).ToArray()));
        else if (Type == "Table")
        {
            var table = new JsonObject();
            foreach (var child in Children)
            {
                ValidateName(child.Name);
                if (table.Any(pair => pair.Key.Equals(child.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Argument '{child.Name}' is duplicated.");
                table.Add(child.Name, child.Build());
            }
            ResourceJson.Set(result, "Value", table);
        }
        else if (Type != _originalType || Text != _originalText)
        {
            ResourceJson.Set(result, "Value", Type switch
            {
                "Null" => null,
                "Boolean" => JsonValue.Create(bool.Parse(Text)),
                "Byte" or "SByte" or "Int16" or "UInt16" or "Int32" or "UInt32" => JsonValue.Create(long.Parse(Text, CultureInfo.InvariantCulture)),
                _ => JsonValue.Create(Text)
            });
        }
        return result;
    }

    private bool Unchanged => Type == _originalType && Text == _originalText && Children.Count == _originalChildren.Length &&
        Children.Select((child, index) => ReferenceEquals(child, _originalChildren[index].Draft) && child.Name == _originalChildren[index].Name && child.Unchanged).All(value => value);

    public JsonObject Save()
    {
        ValidateName(Name);
        var value = Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new JsonObject { ["Argument"] = value.DeepClone() }.ToJsonString()));
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
        try { RabbitTopologyConfiguration.ValidateArgument(configuration.GetSection("Argument")); }
        catch (Exception error) when (error is FormatException or OverflowException)
        { throw new ArgumentException($"Argument '{Name}' contains an invalid {Type} value. {error.Message}", error); }
        return value;
    }

    public static void ValidateName(string name)
    {
        RabbitTopology.Name(name);
        if (name.Contains(':'))
            throw new ArgumentException("Enter an argument name without a colon.");
    }
}
