using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Expectations;

public static class ExpectationsComponentTypes
{
    public static readonly NodeType Expect = new("event.expect");
    public static readonly NodeType Guard = new("event.guard");
}
