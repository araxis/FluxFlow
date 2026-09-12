using FluxFlow.Json.Links;
using System.Text.Json;
using FluxFlow.Core.Addressing;
using FluxFlow.Json.Model;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Json;

internal sealed class DocumentLinks(ApplicationDefinition definition, IFlowExpressionEngine? expressions,
    Func<string, IFlowExpressionEngine?>? resolveExpressions,
    Action<string, string, FlowMessage, Exception>? onRejected)
{
    private readonly ApplicationConditionCompiler _conditions = new(definition, expressions, resolveExpressions);
    private readonly Dictionary<(string Source, string Target, string? Condition, string Parameters), (FlowDeliveryPolicy Delivery, bool Fallback)> _linked = [];

    public void Apply(FlowApplication application, IReadOnlyDictionary<string, FlowComponent> components)
    {
        foreach (var (workflowName, workflow) in definition.Workflows)
        foreach (var (componentName, componentDefinition) in workflow.Components)
        {
            var component = components[$"{workflowName}.{componentName}"];
            var reservedPort = component.Inputs.Keys.Concat(component.Outputs.Keys)
                .FirstOrDefault(ApplicationOutputPolicies.IsPolicy);
            if (reservedPort is not null)
                throw new JsonException($"{component.Address}.{reservedPort}: the name is reserved for an output policy.");
            foreach (var port in ApplicationOutputPolicies.Read(componentDefinition.Properties,
                         CanonicalApplicationProperties.DiscardUnmatched, component.Address, component.Outputs.ContainsKey))
                component.Outputs[port].DiscardUnmatched();
            foreach (var port in ApplicationOutputPolicies.Read(componentDefinition.Properties,
                         CanonicalApplicationProperties.RepliesFrom, component.Address, component.Outputs.ContainsKey))
                application.RepliesFrom(component.Outputs[port]);
            foreach (var (property, value) in componentDefinition.Properties)
            {
                if (ApplicationOutputPolicies.IsPolicy(property)) continue;
                var isInput = component.Inputs.ContainsKey(property);
                var isOutput = component.Outputs.ContainsKey(property);
                if (!isInput && !isOutput)
                {
                    if (property is "Input" or "Output" or "Events")
                        throw new InvalidOperationException($"Component '{component.Address}' has no data port '{property}'.");
                    continue;
                }
                if (isInput && isOutput)
                    throw new InvalidOperationException($"Port property '{component.Address}.{property}' is ambiguous.");
                var parsed = ApplicationLinkDeclarationParser.Parse(value, $"{component.Address}.{property}");
                if (parsed.Errors.Count != 0)
                    throw new JsonException(string.Join("; ", parsed.Errors.Select(e => $"{e.Location}: {e.Reason}")));
                var declared = ApplicationAddress.WorkflowPort(workflowName, componentName, property);
                foreach (var declaration in parsed.Declarations)
                {
                    var reference = ApplicationAddress.ResolvePort(declaration.Port, workflowName);
                    Link(application, isInput ? reference : declared, isInput ? declared : reference,
                        declaration.Condition, declaration.Parameters, declaration.Delivery, declaration.IsFallback);
                }
            }
        }
    }

    private void Link(FlowApplication application, ApplicationAddress source, ApplicationAddress target,
        string? expression, IReadOnlyDictionary<string, JsonElement>? parameters, FlowDeliveryPolicy delivery, bool fallback)
    {
        var signature = JsonSerializer.Serialize(parameters?.OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, JsonElement>());
        var identity = (source.Value, target.Value, expression, signature);
        if (_linked.TryGetValue(identity, out var existing))
        {
            if (existing != (delivery, fallback))
                throw new JsonException($"Link '{source}' to '{target}' declares conflicting delivery or fallback policies.");
            return;
        }
        _linked.Add(identity, (delivery, fallback));
        var output = application.GetOutput(source.Value);
        var input = application.GetInput(target.Value);
        Action<FlowMessage, Exception>? rejected = delivery != FlowDeliveryPolicy.IsolateRejection || onRejected is null ? null :
            (message, error) => onRejected(source.Value, target.Value, message, error);
        if (fallback)
        {
            output.OtherwiseTo(input, delivery, rejected);
            return;
        }
        var compiled = expression is null ? null : _conditions.Compile(source, expression, typeof(FlowValue), parameters);
        output.LinkTo(input, compiled is null ? null : message => compiled.Evaluate(Context(message)), delivery, rejected);
    }

    private static FlowMapContext Context(FlowMessage message) => new()
    {
        Variables = new Dictionary<string, object?>
        {
            ["input"] = message.Value,
            ["payload"] = message.Value,
            ["message"] = message
        }
    };
}
