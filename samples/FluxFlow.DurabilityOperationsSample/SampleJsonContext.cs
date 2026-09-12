using System.Text.Json.Serialization;
using FluxFlow.Data;

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(FlowValue))]
internal sealed partial class SampleJsonContext : JsonSerializerContext;
