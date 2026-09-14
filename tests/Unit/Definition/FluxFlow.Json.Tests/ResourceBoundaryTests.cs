using FluxFlow.Json.Model;
using Shouldly;
using Xunit;

namespace FluxFlow.Json.Tests;

public sealed class ResourceBoundaryTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Resource_documents_require_composition_before_any_binding_runs(bool text, int resourceCount)
    {
        var resources = string.Join(",", Enumerable.Range(1, resourceCount)
            .Select(index => $"\"store{index}\":{{\"Type\":\"test.resource\"}}"));
        var json = "{\"Resources\":{" + resources +
            "},\"Workflows\":{\"main\":{\"processor\":{\"Type\":\"test.process\"}}}}";
        var calls = 0;
        var bindings = new ComponentBindings();
        bindings.Add("test.process", _ =>
        {
            calls++;
            throw new InvalidOperationException("A resource document must not invoke a direct binding.");
        });

        var error = await Should.ThrowAsync<NotSupportedException>(() => text
            ? FlowJson.BuildAsync(json, bindings)
            : FlowJson.BuildAsync(ApplicationDefinitionJson.Deserialize(json), bindings));

        calls.ShouldBe(0);
        error.Message.ShouldContain("optional composition layer");
        error.Message.ShouldContain("BuildApplicationAsync(applicationServices)");
        error.Message.ShouldContain("host dependencies through component bindings and leave Resources empty");
    }
}
