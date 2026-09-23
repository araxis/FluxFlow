using System.Text.Json;
using FluxFlow.Core.Execution;

namespace FluxFlow.Configuration.Links;

/// <summary>A link to or from the port named by the containing configuration property.</summary>
public sealed record LinkConfiguration(string Port, string? Condition = null,
    IReadOnlyDictionary<string, JsonElement>? Parameters = null,
    FlowDeliveryPolicy Delivery = FlowDeliveryPolicy.Reliable, bool Fallback = false, string? Engine = null);
