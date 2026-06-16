using FluxFlow.Components.Control;
using FluxFlow.Components.Mqtt;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mapping;
using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.MqttCompositionSample;

internal static class SampleApplicationDefinition
{
    public static ApplicationDefinition Create()
        => new()
        {
            Resources =
            {
                ["sample-bus"] = new NodeDefinition
                {
                    Type = MqttComponentTypes.Connection,
                    Configuration =
                    {
                        ["profile"] = JsonValue(new
                        {
                            name = "sample-bus",
                            host = "localhost",
                            port = 1883
                        }),
                        ["reconnect"] = JsonValue(new
                        {
                            enabled = true,
                            maxAttempts = 5
                        })
                    }
                }
            },
            Workflows =
            {
                ["main"] = new WorkflowDefinition
                {
                    Nodes =
                    {
                        ["subscribe"] = new NodeDefinition
                        {
                            Type = MqttComponentTypes.Subscribe,
                            Phase = 10,
                            Configuration =
                            {
                                ["connectionName"] = JsonValue("sample-bus"),
                                ["topicFilter"] = JsonValue("orders/input"),
                                ["qualityOfService"] = JsonValue(MqttQualityOfService.AtLeastOnce),
                                ["receiveRetainedMessages"] = JsonValue(true),
                                ["retainAsPublished"] = JsonValue(true),
                                ["boundedCapacity"] = JsonValue(8)
                            }
                        },
                        ["decode"] = new NodeDefinition
                        {
                            Type = MappingComponentTypes.Mapper,
                            Configuration =
                            {
                                ["engine"] = JsonValue("sample"),
                                ["expression"] = JsonValue("decode-order"),
                                ["expressionName"] = JsonValue("decode-order"),
                                ["inputType"] = JsonValue("sample.mqtt.received"),
                                ["outputType"] = JsonValue("sample.order"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [MappingComponentPorts.Input] = JsonValue("subscribe.Output")
                            }
                        },
                        ["active"] = new NodeDefinition
                        {
                            Type = ControlComponentTypes.Filter,
                            Configuration =
                            {
                                ["engine"] = JsonValue("sample"),
                                ["expression"] = JsonValue("order-is-active"),
                                ["expressionName"] = JsonValue("active-orders"),
                                ["inputType"] = JsonValue("sample.order"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [ControlComponentPorts.Input] = JsonValue("decode.Output")
                            }
                        },
                        ["createPublish"] = new NodeDefinition
                        {
                            Type = MappingComponentTypes.Mapper,
                            Configuration =
                            {
                                ["engine"] = JsonValue("sample"),
                                ["expression"] = JsonValue("create-publish-request"),
                                ["expressionName"] = JsonValue("review-publish-request"),
                                ["inputType"] = JsonValue("sample.order"),
                                ["outputType"] = JsonValue("sample.mqtt.publish"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [MappingComponentPorts.Input] = JsonValue("active.Output")
                            }
                        },
                        ["publish"] = new NodeDefinition
                        {
                            Type = MqttComponentTypes.Publish,
                            Configuration =
                            {
                                ["connectionName"] = JsonValue("sample-bus"),
                                ["qualityOfService"] = JsonValue(MqttQualityOfService.AtLeastOnce),
                                ["retain"] = JsonValue(false),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [MqttComponentPorts.Input] = JsonValue("createPublish.Output")
                            }
                        },
                        ["results"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.PublishResultSink,
                            Ports =
                            {
                                ["Input"] = JsonValue("publish.Result")
                            }
                        }
                    }
                }
            }
        };

    private static JsonElement JsonValue<T>(T value)
        => JsonSerializer.SerializeToElement(value);
}
