using FluxFlow.Components.Control;
using FluxFlow.Components.Mapping;
using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.MappingControlSample;

internal static class SampleApplicationDefinition
{
    public static ApplicationDefinition Create()
        => new()
        {
            Workflows =
            {
                ["main"] = new WorkflowDefinition
                {
                    Nodes =
                    {
                        ["source"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.OrderSource,
                            Configuration =
                            {
                                ["orders"] = JsonValue(new[]
                                {
                                    new IncomingOrder("A-100", "Harbor Market", 125m, Active: true),
                                    new IncomingOrder("A-101", "Cedar Supply", 42m, Active: true),
                                    new IncomingOrder("A-102", "Summit Works", 230m, Active: true),
                                    new IncomingOrder("A-103", "Old Town Goods", 18m, Active: false)
                                })
                            }
                        },
                        ["review"] = new NodeDefinition
                        {
                            Type = MappingComponentTypes.Mapper,
                            Configuration =
                            {
                                ["engine"] = JsonValue("sample"),
                                ["expression"] = JsonValue("review-order"),
                                ["expressionName"] = JsonValue("review-order"),
                                ["inputType"] = JsonValue("sample.order.input"),
                                ["outputType"] = JsonValue("sample.order.reviewed"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [MappingComponentPorts.Input] = JsonValue("source.Output")
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
                                ["inputType"] = JsonValue("sample.order.reviewed"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [ControlComponentPorts.Input] = JsonValue("review.Output")
                            }
                        },
                        ["assertTotal"] = new NodeDefinition
                        {
                            Type = ControlComponentTypes.Assert,
                            Configuration =
                            {
                                ["engine"] = JsonValue("sample"),
                                ["expression"] = JsonValue("order-total-valid"),
                                ["expressionName"] = JsonValue("valid-total"),
                                ["name"] = JsonValue("total is valid"),
                                ["failureMessage"] = JsonValue("Order total must be zero or greater."),
                                ["inputType"] = JsonValue("sample.order.reviewed"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [ControlComponentPorts.Input] = JsonValue("active.Output")
                            }
                        },
                        ["route"] = new NodeDefinition
                        {
                            Type = ControlComponentTypes.When,
                            Configuration =
                            {
                                ["engine"] = JsonValue("sample"),
                                ["expression"] = JsonValue("order-is-priority"),
                                ["expressionName"] = JsonValue("priority-route"),
                                ["inputType"] = JsonValue("sample.order.reviewed"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [ControlComponentPorts.Input] = JsonValue("active.Output")
                            }
                        },
                        ["priority"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.OrderSink,
                            Configuration =
                            {
                                ["category"] = JsonValue("priority")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue("route.WhenTrue")
                            }
                        },
                        ["standard"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.OrderSink,
                            Configuration =
                            {
                                ["category"] = JsonValue("standard")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue("route.WhenFalse")
                            }
                        },
                        ["assertions"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.AssertionSink,
                            Ports =
                            {
                                ["Input"] = JsonValue("assertTotal.Result")
                            }
                        }
                    }
                }
            }
        };

    private static JsonElement JsonValue<T>(T value)
        => JsonSerializer.SerializeToElement(value);
}
