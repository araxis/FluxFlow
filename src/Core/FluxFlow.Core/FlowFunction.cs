using FluxFlow.Nodes;

namespace FluxFlow.Core;

/// <summary>A reusable application request with bound input and reply output. Each caller controls waiting through cancellation.</summary>
public delegate Task<FlowMessage> FlowFunction(FlowMessage message, CancellationToken cancellationToken = default);
