using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Metrics.Materialization;

public sealed class MetricSampleInputMaterializer()
    : JsonFlowValueMaterializer<MetricSampleInput>(
        "metrics.sample.invalid_shape",
        "Metrics",
        FlowValueShape.Object("A metric sample object with optional name, group, timestamp, value, size, unit, and tags."));
