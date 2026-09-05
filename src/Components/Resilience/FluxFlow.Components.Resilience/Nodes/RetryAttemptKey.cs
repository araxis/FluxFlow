using FluxFlow.Nodes;

namespace FluxFlow.Components.Resilience.Nodes;

internal readonly record struct RetryAttemptKey(TraceId TraceId, int Attempt);
