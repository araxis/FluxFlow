using FluxFlow.Components.Storage;
using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.StorageCompositionSample;

internal static class SampleApplicationDefinition
{
    public static ApplicationDefinition CreatePut()
        => new()
        {
            Workflows =
            {
                ["put"] = new WorkflowDefinition
                {
                    Nodes =
                    {
                        ["source"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.PutSource,
                            Phase = 1,
                            Configuration =
                            {
                                ["records"] = JsonValue(new[]
                                {
                                    new SamplePutRecord("alpha", "first"),
                                    new SamplePutRecord("beta", "second")
                                })
                            }
                        },
                        ["store"] = new NodeDefinition
                        {
                            Type = StorageComponentTypes.Put,
                            Configuration =
                            {
                                ["collection"] = JsonValue("items"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [StorageComponentPorts.Input] = JsonValue("source.Output")
                            }
                        },
                        ["sink"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.ResultSink,
                            Configuration =
                            {
                                ["stage"] = JsonValue("put")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue("store.Result")
                            }
                        }
                    }
                }
            }
        };

    public static ApplicationDefinition CreateGet()
        => new()
        {
            Workflows =
            {
                ["get"] = new WorkflowDefinition
                {
                    Nodes =
                    {
                        ["source"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.GetSource,
                            Phase = 1,
                            Configuration =
                            {
                                ["keys"] = JsonValue(new[] { "alpha", "missing", "beta" })
                            }
                        },
                        ["store"] = new NodeDefinition
                        {
                            Type = StorageComponentTypes.Get,
                            Configuration =
                            {
                                ["collection"] = JsonValue("items"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [StorageComponentPorts.Input] = JsonValue("source.Output")
                            }
                        },
                        ["found"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.ResultSink,
                            Configuration =
                            {
                                ["stage"] = JsonValue("get-found")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue("store.Found")
                            }
                        },
                        ["notFound"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.ResultSink,
                            Configuration =
                            {
                                ["stage"] = JsonValue("get-not-found")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue("store.NotFound")
                            }
                        }
                    }
                }
            }
        };

    public static ApplicationDefinition CreateQuery()
        => new()
        {
            Workflows =
            {
                ["query"] = new WorkflowDefinition
                {
                    Nodes =
                    {
                        ["source"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.QuerySource,
                            Phase = 1
                        },
                        ["store"] = new NodeDefinition
                        {
                            Type = StorageComponentTypes.Query,
                            Configuration =
                            {
                                ["collection"] = JsonValue("items"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [StorageComponentPorts.Input] = JsonValue("source.Output")
                            }
                        },
                        ["sink"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.QueryResultSink,
                            Configuration =
                            {
                                ["stage"] = JsonValue("query")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue("store.Result")
                            }
                        }
                    }
                }
            }
        };

    public static ApplicationDefinition CreateDelete()
        => new()
        {
            Workflows =
            {
                ["delete"] = new WorkflowDefinition
                {
                    Nodes =
                    {
                        ["source"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.DeleteSource,
                            Phase = 1,
                            Configuration =
                            {
                                ["keys"] = JsonValue(new[] { "alpha", "missing" })
                            }
                        },
                        ["store"] = new NodeDefinition
                        {
                            Type = StorageComponentTypes.Delete,
                            Configuration =
                            {
                                ["collection"] = JsonValue("items"),
                                ["boundedCapacity"] = JsonValue(8)
                            },
                            Ports =
                            {
                                [StorageComponentPorts.Input] = JsonValue("source.Output")
                            }
                        },
                        ["sink"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.ResultSink,
                            Configuration =
                            {
                                ["stage"] = JsonValue("delete")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue("store.Result")
                            }
                        }
                    }
                }
            }
        };

    private static JsonElement JsonValue<T>(T value)
        => JsonSerializer.SerializeToElement(value);
}
