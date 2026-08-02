using System.Text.Json;
using FluxFlow.ComponentPackageTemplate.Contracts;
using FluxFlow.ComponentPackageTemplate.Diagnostics;
using FluxFlow.ComponentPackageTemplate.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.ComponentPackageTemplate.Nodes;

/// <summary>
/// Template for authoring a standalone FluxFlow component node. Copy this package, rename the
/// types, and implement <see cref="ProcessAsync"/> — that is the whole job. A node is a
/// self-contained TPL Dataflow processor over the <c>FluxFlow.Nodes</c> kit, with no engine:
/// <list type="bullet">
/// <item>derive from <see cref="FlowNode{TInput, TOutput}"/>;</item>
/// <item>take the node's real dependencies (its options, a <see cref="TimeProvider"/>, an
/// injected client, …) directly in a public constructor — no factories, no registration glue;</item>
/// <item>do the work in <see cref="ProcessAsync"/> on <c>message.Value</c> and
/// <c>await EmitAsync(message.With(result), Stopping)</c> so the correlation id flows downstream;</item>
/// <item>emit domain failures as <see cref="FlowError"/> values on <c>Output</c> and diagnostics on <c>Events</c>.</item>
/// </list>
/// It works with nothing but <c>new TemplateEnrichNode(options)</c> — post to <c>Input</c>,
/// link <c>Output</c>. Composing a graph (read config, new the nodes, LinkTo) is a separate
/// layer.
/// </summary>
public sealed class TemplateEnrichNode : FlowNode<TemplateInput, TemplateOutput>
{
    public const string Succeeded = TemplateDiagnosticNames.EnrichSucceeded;
    public const string Failed = TemplateDiagnosticNames.EnrichFailed;

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

    protected override async Task ProcessAsync(FlowMessage<TemplateInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Value;

        if (string.IsNullOrWhiteSpace(input.Value))
        {
            // A bad input remains ordinary workflow data; the envelope keeps its identity.
            await EmitAsync(
                    message.WithError<TemplateOutput>(new FlowError(
                        TemplateErrorCodes.EnrichFailed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "template.enrich requires a non-empty input value.",
                        "template",
                        details: JsonSerializer.SerializeToElement(new { input.Id }))),
                    Stopping)
                .ConfigureAwait(false);
            EmitEvent(Diagnostic(message, Failed, FlowEventLevel.Warning, "template.enrich skipped an input value."));
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
        await EmitAsync(message.With(output), Stopping).ConfigureAwait(false);
        EmitEvent(Diagnostic(message, Succeeded, FlowEventLevel.Information, "template.enrich emitted an output value."));
    }

    private FlowEvent Diagnostic(
        FlowMessage<TemplateInput> message,
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
                ["id"] = message.Value.Id,
                ["prefix"] = _options.Prefix,
                ["boundedCapacity"] = _options.BoundedCapacity
            }
        };
}
