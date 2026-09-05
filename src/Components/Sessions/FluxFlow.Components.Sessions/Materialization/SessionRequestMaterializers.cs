using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Data;

namespace FluxFlow.Components.Sessions.Materialization;

public sealed class SessionContentRecordInputMaterializer()
    : JsonFlowValueMaterializer<SessionContentRecordInput>(
        "sessions.record.invalid_shape",
        "Sessions",
        FlowValueShape.Object("A session record object with content and optional timestamp, type, name, and attributes.", "content"));

public sealed class SessionQueryRequestMaterializer()
    : JsonFlowValueMaterializer<SessionQueryRequest>(
        "sessions.query.invalid_shape",
        "Sessions",
        FlowValueShape.Object("A session query object with optional names, tags, time ranges, state filters, correlation, and limit."));
