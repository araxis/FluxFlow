using FluxFlow.ComponentPackageTemplate.Contracts;
using FluxFlow.ComponentPackageTemplate.Diagnostics;
using FluxFlow.ComponentPackageTemplate.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.ComponentPackageTemplate.Nodes;

public sealed class TemplateEnrichNode : FlowNodeBase
{
    private readonly TemplateEnrichOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly TransformManyBlock<TemplateInput, TemplateOutput> _input;

    private TemplateEnrichNode(
        TemplateEnrichOptions options,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _input = new TransformManyBlock<TemplateInput, TemplateOutput>(
            Enrich,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                EnsureOrdered = true
            });
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<TemplateInput> Input => _input;

    public ISourceBlock<TemplateOutput> Output => _input;

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        TemplateComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var node = new TemplateEnrichNode(
            TemplateOptionsReader.ReadEnrichOptions(context.Definition),
            componentOptions.TimeProvider);

        return context.CreateNode(node)
            .Input(TemplateComponentPorts.Input, node.Input)
            .Output(TemplateComponentPorts.Output, node.Output)
            .Build();
    }

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
        }
    }

    private IReadOnlyCollection<TemplateOutput> Enrich(TemplateInput input)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(input);
            if (string.IsNullOrWhiteSpace(input.Value))
            {
                throw new InvalidOperationException(
                    "template.enrich requires a non-empty input value.");
            }

            var output = new TemplateOutput
            {
                Id = input.Id,
                Value = input.Value,
                Text = $"{_options.Prefix}:{input.Value}",
                ProcessedAt = _timeProvider.GetUtcNow()
            };

            TryEmitDiagnostic(
                TemplateDiagnosticNames.EnrichSucceeded,
                message: "template.enrich emitted an output value.",
                attributes: CreateAttributes(input));

            return [output];
        }
        catch (Exception exception)
        {
            TryReportError(
                TemplateErrorCodes.EnrichFailed,
                exception.Message,
                exception,
                CreateErrorContext(input));
            TryEmitDiagnostic(
                TemplateDiagnosticNames.EnrichFailed,
                FlowDiagnosticLevel.Warning,
                "template.enrich skipped an input value.",
                exception,
                CreateAttributes(input));

            return [];
        }
    }

    private string? CreateErrorContext(TemplateInput? input)
        => input is null ? null : $"id={input.Id}";

    private Dictionary<string, object?> CreateAttributes(TemplateInput? input)
        => new(StringComparer.Ordinal)
        {
            ["id"] = input?.Id,
            ["prefix"] = _options.Prefix,
            ["boundedCapacity"] = _options.BoundedCapacity
        };
}
