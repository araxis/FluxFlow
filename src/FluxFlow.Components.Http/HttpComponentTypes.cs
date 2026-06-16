using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Http;

public static class HttpComponentTypes
{
    public static readonly NodeType Client = new("http.client");
    public static readonly NodeType Request = new("http.request");
}
