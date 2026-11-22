using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;
using NATS.Client.Core;
using Microsoft.Extensions.Primitives;

namespace FluxFlow.Components.Nats;

public sealed record NatsMessage
{
    public string Subject { get; init; } = "";
    public string? ReplyTo { get; init; }
    public string DataBase64 { get; init; } = "";
    public Dictionary<string, string[]> Headers { get; init; } = new(StringComparer.Ordinal);

    internal byte[] Bytes(int maximum)
    {
        ArgumentNullException.ThrowIfNull(DataBase64);
        if (DataBase64.Length > ((long)maximum + 2) / 3 * 4) throw new ArgumentException("NATS payload exceeds MaxMessageBytes.");
        var bytes = Convert.FromBase64String(DataBase64);
        if (bytes.Length > maximum) throw new ArgumentException("NATS payload exceeds MaxMessageBytes.");
        return bytes;
    }
    internal NatsHeaders NativeHeaders()
    {
        ArgumentNullException.ThrowIfNull(Headers);
        var headers = new NatsHeaders();
        foreach (var pair in Headers)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Any(c => c <= 32 || c >= 127 || c == ':') || pair.Value is null)
                throw new ArgumentException("Invalid NATS header name or values.");
            foreach (var value in pair.Value)
                if (value is null || value.Contains('\r') || value.Contains('\n')) throw new ArgumentException("NATS header values cannot contain line breaks.");
            headers.Add(pair.Key, new StringValues(pair.Value));
        }
        return headers;
    }
    internal static NatsMessage FromNative(NatsMsg<byte[]> message, int maximum)
    {
        message.EnsureSuccess();
        var bytes = message.Data ?? [];
        if (bytes.Length > maximum) throw new ArgumentException("NATS delivery exceeds MaxMessageBytes.");
        return new() { Subject = message.Subject, ReplyTo = message.ReplyTo, DataBase64 = Convert.ToBase64String(bytes),
            Headers = CopyHeaders(message.Headers) };
    }
    internal static Dictionary<string, string[]> CopyHeaders(NatsHeaders? headers) =>
        headers?.ToDictionary(pair => pair.Key, pair => pair.Value.Select(value => value ?? "").ToArray(), StringComparer.Ordinal)
        ?? new(StringComparer.Ordinal);

    internal static NatsMessage Read(FlowMessage message)
    {
        var json = message.Value.Json;
        var request = json.Deserialize(NatsMessageJson.Default.NatsMessage)
            ?? throw new ArgumentException("Expected a NATS message object.");
        // Missing init-only fields can deserialize as null. An explicit null is still invalid.
        return request.Headers is null && !json.TryGetProperty(nameof(Headers), out _)
            ? request with { Headers = new(StringComparer.Ordinal) } : request;
    }
    internal JsonFlowValue Value() => new(JsonSerializer.SerializeToElement(this, NatsMessageJson.Default.NatsMessage));
}

[JsonSerializable(typeof(NatsMessage))]
internal partial class NatsMessageJson : JsonSerializerContext;
