using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class CompositionValidatorTests
{
    [Fact]
    public void Validator_reports_unknown_node_type()
    {
        var definition = CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow.Node("source", "missing.type"))
            .Build();

        var result = new CompositionValidator().Validate(definition, TestCompositionRegistry.Create());

        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.UnknownNodeType);
    }

    [Fact]
    public void Validator_reports_missing_ports()
    {
        var definition = CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow
                .Node("source", TestNodeTypes.Source)
                .Node("sink", TestNodeTypes.Sink)
                .Link("source.Missing", "sink.Input")
                .Link("source.Output", "sink.Missing"))
            .Build();

        var result = new CompositionValidator().Validate(definition, TestCompositionRegistry.Create());

        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.MissingOutputPort);
        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.MissingInputPort);
    }

    [Fact]
    public void Validator_reports_duplicate_links()
    {
        var definition = CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow
                .Node("source", TestNodeTypes.Source)
                .Node("sink", TestNodeTypes.Sink)
                .Link("source.Output", "sink.Input")
                .Link("source.Output", "sink.Input"))
            .Build();

        var result = new CompositionValidator().Validate(definition, TestCompositionRegistry.Create());

        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.DuplicateLink);
    }

    [Fact]
    public void Validator_reports_type_mismatch_when_metadata_exposes_types()
    {
        var definition = CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow
                .Node("source", TestNodeTypes.IntSource)
                .Node("sink", TestNodeTypes.Sink)
                .Link("source.Output", "sink.Input"))
            .Build();

        var result = new CompositionValidator().Validate(definition, TestCompositionRegistry.Create());

        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.PortTypeMismatch);
    }
}
