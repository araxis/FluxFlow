using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Core.Messaging;

/// <summary>Generated JSON metadata for Core envelopes and values. Hosts can combine this
/// context with their own JsonSerializerContext for additional payload types.</summary>
[JsonSerializable(typeof(FlowMessage))]
[JsonSerializable(typeof(FlowError))]
[JsonSerializable(typeof(FlowValue))]
[JsonSerializable(typeof(JsonFlowValue))]
[JsonSerializable(typeof(TraceId))]
[JsonSerializable(typeof(MessageId))]
[JsonSerializable(typeof(MessageId?))]
[JsonSerializable(typeof(CorrelationId?))]
[JsonSerializable(typeof(Dictionary<string, FlowValue>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, FlowValue>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(ImmutableDictionary<string, double>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(char))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Guid))]
public sealed partial class FlowJsonContext : JsonSerializerContext;
