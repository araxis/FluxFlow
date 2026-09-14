using System.Text.Json;
using FluxFlow.Data;

namespace FluxFlow.Nodes;

internal static class FlowProcessingError
{
    internal static FlowError FromException(Exception exception) => new(
        "node.processing_failed",
        exception.Message,
        "processing",
        exception is TimeoutException,
        JsonSerializer.SerializeToElement(new { exceptionType = exception.GetType().FullName }));
}
