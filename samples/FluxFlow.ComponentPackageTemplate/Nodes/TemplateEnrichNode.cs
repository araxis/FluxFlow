using System.Text.Json;
using FluxFlow.ComponentPackageTemplate.Contracts;
using FluxFlow.ComponentPackageTemplate.Diagnostics;
using FluxFlow.ComponentPackageTemplate.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.ComponentPackageTemplate.Nodes;

/// <summary>
/// A directly constructed component with canonical ports and a component-local input model.
/// The base owns bounded processing and lifetime; this class owns enrichment and diagnostics.
/// </summary>
public sealed class TemplateEnrichNode : FlowNode
{
    public const string Succeeded = TemplateDiagnosticNames.EnrichSucceeded;
    public const string Failed = TemplateDiagnosticNames.EnrichFailed;

    private static readonly InputMaterializer Materializer = new();
    private readonly TemplateEnrichOptions _options;
    private readonly TimeProvider _clock;

    public TemplateEnrichNode(TemplateEnrichOptions options, TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            // One explicit component capacity bounds both intake and reliable output.
            InputCapacity = (options ?? throw new ArgumentNullException(nameof(options))).BoundedCapacity,
            OutputCapacity = options.BoundedCapacity
        })
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ProcessAsync(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var materialized = Materializer.Materialize(message.Value!);
        if (!materialized.IsSuccess)
        {
            await EmitAsync(message.WithError(materialized.Error!), Stopping).ConfigureAwait(false);
            EmitEvent(Diagnostic(message, null, Failed, FlowEventLevel.Warning, "template.enrich could not read its input."));
            return;
        }
        var input = materialized.Value;

        if (string.IsNullOrWhiteSpace(input.Value))
        {
            // A bad input remains ordinary workflow data; the envelope keeps its identity.
            await EmitAsync(
                    message.WithError(new FlowError(
                        TemplateErrorCodes.EnrichFailed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "template.enrich requires a non-empty input value.",
                        "template",
                        details: JsonSerializer.SerializeToElement(new { input.Id }))),
                    Stopping)
                .ConfigureAwait(false);
            EmitEvent(Diagnostic(message, input, Failed, FlowEventLevel.Warning, "template.enrich skipped an input value."));
            return;
        }

        var output = new TemplateOutput
        {
            Id = input.Id,
            Value = input.Value,
            Text = $"{_options.Prefix}:{input.Value}",
            ProcessedAt = _clock.GetUtcNow()
        };

        // Carry the correlation id (and headers) forward onto the enriched payload.
        await EmitAsync(message.With(FlowValue.From(output)), Stopping).ConfigureAwait(false);
        EmitEvent(Diagnostic(message, input, Succeeded, FlowEventLevel.Information, "template.enrich emitted an output value."));
    }

    private FlowEvent Diagnostic(
        FlowMessage message,
        TemplateInput? input,
        string name,
        FlowEventLevel level,
        string description)
        => new()
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = description,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = input?.Id,
                ["prefix"] = _options.Prefix,
                ["boundedCapacity"] = _options.BoundedCapacity
            }
        };

    private sealed class InputMaterializer() : JsonFlowValueMaterializer<TemplateInput>(
        TemplateErrorCodes.EnrichFailed.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "template", FlowValueShape.Any("An enrichment input with Id and Value."));

}
