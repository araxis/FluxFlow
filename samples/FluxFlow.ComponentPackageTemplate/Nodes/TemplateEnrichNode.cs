using FluxFlow.ComponentPackageTemplate.Contracts;
using FluxFlow.ComponentPackageTemplate.Diagnostics;
using FluxFlow.ComponentPackageTemplate.Options;
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
/// <item>do the work in <see cref="ProcessAsync"/> on <c>message.Payload</c> and
/// <c>Emit(message.With(result))</c> so the correlation id flows downstream;</item>
/// <item>report domain failures on <c>Errors</c> and diagnostics on <c>Events</c>.</item>
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
            // BoundedCapacity flows to the kit's bounded input buffer (backpressure on intake);
            // the base constructor validates it (> 0).
            InputCapacity = (options ?? throw new ArgumentNullException(nameof(options))).BoundedCapacity
        })
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    protected override Task ProcessAsync(FlowMessage<TemplateInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;

        if (string.IsNullOrWhiteSpace(input.Value))
        {
            // A bad input is surfaced on Errors (stamped with the in-flight correlation id);
            // the node keeps processing later messages instead of faulting the whole pump.
            EmitError(new FlowError
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Code = TemplateErrorCodes.EnrichFailed,
                Message = "template.enrich requires a non-empty input value.",
                Context = $"id={input.Id}"
            });
            EmitEvent(Diagnostic(message, Failed, FlowEventLevel.Warning, "template.enrich skipped an input value."));
            return Task.CompletedTask;
        }

        var output = new TemplateOutput
        {
            Id = input.Id,
            Value = input.Value,
            Text = $"{_options.Prefix}:{input.Value}",
            ProcessedAt = _clock.GetUtcNow()
        };

        // Carry the correlation id (and headers) forward onto the enriched payload.
        Emit(message.With(output));
        EmitEvent(Diagnostic(message, Succeeded, FlowEventLevel.Information, "template.enrich emitted an output value."));
        return Task.CompletedTask;
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
                ["id"] = message.Payload.Id,
                ["prefix"] = _options.Prefix,
                ["boundedCapacity"] = _options.BoundedCapacity
            }
        };
}
