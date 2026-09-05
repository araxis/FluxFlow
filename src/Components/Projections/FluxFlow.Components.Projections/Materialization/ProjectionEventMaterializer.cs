using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Projections.Materialization;

public sealed class ProjectionEventMaterializer()
    : JsonFlowValueMaterializer<ProjectionEvent>(
        "projection.event.invalid_shape",
        "Projections",
        FlowValueShape.Object(
            "A projection event object with timestamp, type, source, and optional event details.",
            "timestamp",
            "type",
            "source"));
