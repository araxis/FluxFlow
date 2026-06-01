using FluxFlow.Components.Mapping;
using FluxFlow.Components.Observability;
using FluxFlow.Components.State;
using FluxFlow.Components.Timers;
using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.StateCompositionSample;

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
                        ["timer"] = new NodeDefinition
                        {
                            Type = TimerComponentTypes.Interval,
                            Configuration =
                            {
                                ["name"] = JsonValue("sample-timer"),
                                ["intervalMilliseconds"] = JsonValue(10),
                                ["emitImmediately"] = JsonValue(true),
                                ["maxTicks"] = JsonValue(3),
                                ["boundedCapacity"] = JsonValue(8)
                            }
                        },
                        ["toStateInput"] = new NodeDefinition
                        {
                            Type = MappingComponentTypes.Mapper,
                            Configuration =
                            {
                                ["engine"] = JsonValue("sample"),
                                ["expression"] = JsonValue("tick-to-state-input"),
                                ["expressionName"] = JsonValue("tick-to-state-input"),
                                ["inputType"] = JsonValue("sample.timer.tick"),
                                ["outputType"] = JsonValue("sample.state.input"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [MappingComponentPorts.Input] = JsonValue("timer.Output")
                            }
                        },
                        ["state"] = new NodeDefinition
                        {
                            Type = StateComponentTypes.Reducer,
                            Configuration =
                            {
                                ["engine"] = JsonValue("sample"),
                                ["reducer"] = JsonValue("count-ticks"),
                                ["expressionName"] = JsonValue("count-ticks"),
                                ["initialState"] = JsonValue(0),
                                ["boundedCapacity"] = JsonValue(8),
                                ["maxKeys"] = JsonValue(4)
                            },
                            Ports =
                            {
                                [StateComponentPorts.Input] = JsonValue("toStateInput.Output")
                            }
                        },
                        ["counter"] = new NodeDefinition
                        {
                            Type = ObservabilityComponentTypes.Counter,
                            Configuration =
                            {
                                ["name"] = JsonValue("state-updates"),
                                ["inputType"] = JsonValue("sample.state.result"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [ObservabilityComponentPorts.Input] = JsonValue("state.Output")
                            }
                        },
                        ["stateSink"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.StateSink,
                            Ports =
                            {
                                ["Input"] = JsonValue("state.Output")
                            }
                        },
                        ["counterSink"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.CounterSink,
                            Ports =
                            {
                                ["Input"] = JsonValue("counter.Snapshots")
                            }
                        }
                    }
                }
            }
        };

    private static JsonElement JsonValue<T>(T value)
        => JsonSerializer.SerializeToElement(value);
}
