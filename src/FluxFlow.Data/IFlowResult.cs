namespace FluxFlow.Data;

public interface IFlowResult
{
    string Kind { get; }

    bool IsError { get; }

    FlowError? Error { get; }

    DateTimeOffset Timestamp { get; }
}
