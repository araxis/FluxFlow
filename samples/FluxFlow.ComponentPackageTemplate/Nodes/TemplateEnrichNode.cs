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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
        TemplateInput input;
        try
        {
            input = message.Value!.Json.Deserialize<TemplateInput>(JsonOptions)
                ?? throw new JsonException("TemplateInput cannot be null.");
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or NotSupportedException)
        {
            var error = new FlowError(
                TemplateErrorCodes.EnrichFailed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "The workflow value cannot be materialized as TemplateInput.",
                "template",
                details: JsonSerializer.SerializeToElement(new { target = nameof(TemplateInput), reason = exception.Message }));
            await EmitAsync(message.WithError(error), Stopping).ConfigureAwait(false);
            EmitEvent(Diagnostic(message, null, Failed, FlowEventLevel.Warning, "template.enrich could not read its input."));
            return;
        }

        if (string.IsNullOrWhiteSpace(input.Value))
        {
            // A bad input becomes error data with a link to the original message.
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
        await EmitAsync(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(output, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Stopping).ConfigureAwait(false);
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

}
