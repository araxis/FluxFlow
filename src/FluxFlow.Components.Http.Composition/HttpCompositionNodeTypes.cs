using FluxFlow.Composition;

namespace FluxFlow.Components.Http.Composition;

public static class HttpCompositionNodeTypes
{
    public const string Client = "http.request";
    public const string LegacyClient = "http.client";

    internal static CompositionComponentTypeDescriptor ClientDescriptor { get; } =
        new(
            Client,
            [LegacyClient],
            CompositionProcessingCapabilities.ParallelRelaxedOrder);
}
