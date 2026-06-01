using FluxFlow.Components.Sessions;
using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.SessionsCompositionSample;

internal static class SampleApplicationDefinition
{
    private const string SessionId = "sample-session";

    public static ApplicationDefinition CreateRecording()
        => new()
        {
            Workflows =
            {
                ["record"] = new WorkflowDefinition
                {
                    Nodes =
                    {
                        ["source"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.Source,
                            Phase = 1,
                            Configuration =
                            {
                                ["records"] = JsonValue(CreateRecords())
                            }
                        },
                        ["recorder"] = new NodeDefinition
                        {
                            Type = SessionsComponentTypes.Recorder,
                            Configuration =
                            {
                                ["sessionId"] = JsonValue(SessionId),
                                ["name"] = JsonValue("Sample session"),
                                ["boundedCapacity"] = JsonValue(8),
                                ["tags"] = JsonValue(new Dictionary<string, string>
                                {
                                    ["sample"] = "sessions-composition"
                                })
                            },
                            Ports =
                            {
                                [SessionsComponentPorts.Input] = JsonValue("source.Output")
                            }
                        },
                        ["recorded"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.Sink,
                            Configuration =
                            {
                                ["stage"] = JsonValue("recorded")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue("recorder.Output")
                            }
                        }
                    }
                }
            }
        };

    public static ApplicationDefinition CreateReplay()
        => new()
        {
            Workflows =
            {
                ["replay"] = new WorkflowDefinition
                {
                    Nodes =
                    {
                        ["source"] = new NodeDefinition
                        {
                            Type = SessionsComponentTypes.Replay,
                            Configuration =
                            {
                                ["sessionId"] = JsonValue(SessionId),
                                ["mode"] = JsonValue("instant"),
                                ["boundedCapacity"] = JsonValue(8)
                            }
                        },
                        ["replayed"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.Sink,
                            Configuration =
                            {
                                ["stage"] = JsonValue("replayed")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue("source.Output")
                            }
                        }
                    }
                }
            }
        };

    private static IReadOnlyList<SessionRecordInput> CreateRecords()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        return
        [
            CreateRecord(start, "created", "order-100"),
            CreateRecord(start.AddSeconds(2), "updated", "order-100"),
            CreateRecord(start.AddSeconds(4), "closed", "order-100")
        ];
    }

    private static SessionRecordInput CreateRecord(
        DateTimeOffset timestamp,
        string name,
        string payload)
        => new()
        {
            Timestamp = timestamp,
            Type = "sample.order",
            Name = name,
            Payload = payload,
            ContentType = "text/plain",
            Attributes = new Dictionary<string, string>
            {
                ["source"] = "sample"
            }
        };

    private static JsonElement JsonValue<T>(T value)
        => JsonSerializer.SerializeToElement(value);
}
