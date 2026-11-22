using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Components.Logging;

/// <summary>A structured log operation. JSON values are preserved until passed to the logging API.</summary>
public sealed class LogRequest : FlowValue
{
    private IReadOnlyList<FlowValue?> _args = Array.Empty<FlowValue?>();
    private IReadOnlyDictionary<string, FlowValue?> _attributes = new ReadOnlyDictionary<string, FlowValue?>(new Dictionary<string, FlowValue?>());
    public required string MessageTemplate { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter<LogLevel>))]
    public required LogLevel LogLevel { get; init; }
    public IReadOnlyList<FlowValue?> Args
    {
        get => _args;
        init => _args = Array.AsReadOnly(value?.ToArray() ?? []);
    }
    public IReadOnlyDictionary<string, FlowValue?> Attributes
    {
        get => _attributes;
        init => _attributes = new ReadOnlyDictionary<string, FlowValue?>(value is null ? new() : new Dictionary<string, FlowValue?>(value, StringComparer.Ordinal));
    }
    /// <summary>Optional native exception for typed requests. JSON callers can supply error details in Attributes.</summary>
    [JsonIgnore]
    public Exception? Exception { get; init; }

    protected override JsonElement CreateJson() => JsonSerializer.SerializeToElement(this, JsonOptions);
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = null };
}
