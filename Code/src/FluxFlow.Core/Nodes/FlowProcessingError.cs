using System.Text.Json;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Core.Nodes;

internal static class FlowProcessingError
{
    internal static FlowError FromException(Exception exception) => new(
        "node.processing_failed",
        exception.Message,
        "processing",
        exception is TimeoutException,
        new JsonFlowValue(JsonSerializer.SerializeToElement(
            new Dictionary<string, string?> { ["exceptionType"] = exception.GetType().FullName },
            typeof(Dictionary<string, string?>), FlowJsonContext.Default)));
}
