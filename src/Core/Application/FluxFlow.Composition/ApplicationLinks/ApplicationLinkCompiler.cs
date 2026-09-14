using FluxFlow.Composition.Definition;
using FluxFlow.Json.Links;
using System.Text.Json;
using FluxFlow.Core.Addressing;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Json.Model;
using FluxFlow.Mapping;

namespace FluxFlow.Composition.Links;

public sealed class ApplicationLinkCompiler
{
    private readonly IReadOnlyDictionary<string, ComponentInstance> _instances;
    private readonly IFlowExpressionEngine? _conditionEngine;
    private readonly Func<string, IFlowExpressionEngine?>? _resolveConditionEngine;
    private readonly IReadOnlyDictionary<ApplicationAddress, ComponentPortMetadata> _systemOutputs;

    public ApplicationLinkCompiler(IReadOnlyDictionary<string, ComponentInstance> instances,
        IFlowExpressionEngine? conditionEngine = null,
        IEnumerable<ApplicationSystemOutputMetadata>? systemOutputs = null,
        Func<string, IFlowExpressionEngine?>? resolveConditionEngine = null)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _conditionEngine = conditionEngine;
        _resolveConditionEngine = resolveConditionEngine;
        _systemOutputs = CopySystemOutputs(systemOutputs);
    }

    public ApplicationLinkCompilationResult Compile(ApplicationDefinition definition)
        => Compile(new ApplicationSetup(definition));

    public ApplicationLinkCompilationResult Compile(ApplicationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var definition = setup.Definition;

        var diagnostics = new List<ApplicationLinkDiagnostic>();
        var components = IndexComponents(definition, diagnostics);
        var discardedOutputs = CollectOutputPolicies(components, CanonicalApplicationProperties.DiscardUnmatched, diagnostics);
        var conditionCache = new ApplicationConditionCompiler(definition, _conditionEngine, _resolveConditionEngine);
        var candidates = new List<LinkCandidate>();
        var declarations = new List<ApplicationLinkDeclarationProjection>();

        CollectDeclarations(
            components,
            conditionCache,
            candidates,
            declarations,
            diagnostics);
        CollectCodeLinks(
            setup.Links,
            components,
            conditionCache,
            candidates,
            declarations,
            diagnostics);

        var normalized = RemoveDuplicates(candidates, diagnostics);
        foreach (var group in normalized.Where(candidate => candidate.Link.IsFallback).GroupBy(candidate => candidate.Link.Source))
        {
            if (group.Count() > 1)
                diagnostics.Add(CreateDiagnostic(ApplicationLinkDiagnosticCode.InvalidLinkDeclaration,
                    $"Output '{group.Key}' has more than one fallback.", group.First().Context, source: group.Key));
        }
        ValidateExclusiveClaims(normalized, components, diagnostics);
        ValidateAcyclic(normalized, components, diagnostics);

        var links = normalized
            .Select(static candidate => candidate.Link)
            .OrderBy(static link => link.Source.Value, StringComparer.Ordinal)
            .ThenBy(static link => link.Target.Value, StringComparer.Ordinal)
            .ThenBy(static link => link.ConditionExpression, StringComparer.Ordinal)
            .ThenBy(static link => link.DeclarationSide)
            .ToArray();

        return new ApplicationLinkCompilationResult(links, declarations, diagnostics, discardedOutputs);
    }

    private static IReadOnlyList<ApplicationAddress> CollectOutputPolicies(
        IReadOnlyDictionary<ComponentKey, RegisteredComponent> components, string policy,
        List<ApplicationLinkDiagnostic> diagnostics)
    {
        var outputs = new List<ApplicationAddress>();
        foreach (var (key, component) in components.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
        {
            if (component.Inputs is null) continue;
            try
            {
                foreach (var port in ApplicationOutputPolicies.Read(component.Definition.Properties,
                             policy, key.Value, component.Outputs!.ContainsKey))
                    outputs.Add(ApplicationAddress.WorkflowPort(key.Workflow, key.Component, port));
            }
            catch (JsonException error)
            {
                diagnostics.Add(CreateDiagnostic(ApplicationLinkDiagnosticCode.InvalidLinkDeclaration,
                    error.Message, new DeclarationContext(key.Workflow, key.Component, policy), exception: error));
            }
        }
        return outputs;
    }

    public static JsonElement SerializeDeclarations(
        IEnumerable<ApplicationLinkDeclarationProjection> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var values = declarations.ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException(
                "At least one link declaration is required.",
                nameof(declarations));
        }

        var declaredPort = values[0].DeclaredPort;
        if (values.Any(value => value.DeclaredPort != declaredPort))
        {
            throw new ArgumentException(
                "All serialized links must belong to the same declaration port.",
                nameof(declarations));
        }

        return ApplicationLinkDeclarationParser.Serialize(values
            .Select(static value => new ParsedApplicationLinkDeclaration(
                value.PortReference,
                value.ConditionExpression, value.Parameters, value.Delivery, value.IsFallback))
            .ToArray());
    }

    private void CollectDeclarations(
        IReadOnlyDictionary<ComponentKey, RegisteredComponent> components,
        ApplicationConditionCompiler conditionCache,
        List<LinkCandidate> candidates,
        List<ApplicationLinkDeclarationProjection> projections,
        List<ApplicationLinkDiagnostic> diagnostics)
    {

        foreach (var (componentKey, component) in components.OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal))
        {
            if (component.Inputs is null)
                continue;

            foreach (var property in component.Definition.Properties.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                if (ApplicationOutputPolicies.IsPolicy(property.Key)) continue;
                var isInput = component.Inputs.ContainsKey(property.Key);
                var isOutput = component.Outputs!.ContainsKey(property.Key);
                var context = new DeclarationContext(componentKey.Workflow, componentKey.Component, property.Key);

                if (!isInput && !isOutput)
                    continue;

                if (isInput && isOutput)
                {
                    diagnostics.Add(CreateDiagnostic(
                        ApplicationLinkDiagnosticCode.AmbiguousPortProperty,
                        $"Property '{context.Value}' matches both an input and an output port.",
                        context));
                    continue;
                }

                var side = isInput
                    ? ApplicationLinkDeclarationSide.Input
                    : ApplicationLinkDeclarationSide.Output;

                var parsed = ApplicationLinkDeclarationParser.Parse(property.Value, context.Value);
                var propertyProjections = new List<ApplicationLinkDeclarationProjection>(parsed.Declarations.Count);
                var canProjectProperty = parsed.Errors.Count == 0 && parsed.Declarations.Count > 0;
                foreach (var error in parsed.Errors)
                {
                    diagnostics.Add(CreateDiagnostic(
                        ApplicationLinkDiagnosticCode.InvalidLinkDeclaration,
                        $"Link declaration '{error.Location}' is invalid: {error.Reason}.",
                        context));
                }

                foreach (var declaration in parsed.Declarations)
                {
                    ApplicationAddress reference;
                    try
                    {
                        reference = ApplicationAddress.ResolvePort(declaration.Port, componentKey.Workflow);
                    }
                    catch (Exception exception) when (exception is ArgumentException or FormatException)
                    {
                        canProjectProperty = false;
                        diagnostics.Add(CreateDiagnostic(
                            ApplicationLinkDiagnosticCode.InvalidPortReference,
                            $"Link declaration '{context.Value}' has invalid port reference '{declaration.Port}': {exception.Message}",
                            context,
                            exception: exception));
                        continue;
                    }

                    var declaredPort = ApplicationAddress.WorkflowPort(
                        componentKey.Workflow,
                        componentKey.Component,
                        property.Key);
                    var source = side == ApplicationLinkDeclarationSide.Input ? reference : declaredPort;
                    var target = side == ApplicationLinkDeclarationSide.Input ? declaredPort : reference;

                    if (ApplicationLinkDeclarationProjection.CanProject(source, target, side))
                    {
                        propertyProjections.Add(new ApplicationLinkDeclarationProjection(
                            source,
                            target,
                            declaration.Condition,
                            side, declaration.Parameters, declaration.Delivery, declaration.IsFallback));
                    }
                    else
                    {
                        canProjectProperty = false;
                    }

                    var candidate = CompileLink(
                        new LinkCompilationInput(source, target, declaration.Condition, side,
                            Parameters: declaration.Parameters, Delivery: declaration.Delivery, IsFallback: declaration.IsFallback),
                        context,
                        components,
                        conditionCache,
                        diagnostics);
                    if (candidate is not null)
                        candidates.Add(candidate);
                }

                if (canProjectProperty)
                    projections.AddRange(propertyProjections);
            }
        }
    }

    private void CollectCodeLinks(
        IReadOnlyList<ApplicationLinkDefinition> links,
        IReadOnlyDictionary<ComponentKey, RegisteredComponent> components,
        ApplicationConditionCompiler conditionCache,
        List<LinkCandidate> candidates,
        List<ApplicationLinkDeclarationProjection> projections,
        List<ApplicationLinkDiagnostic> diagnostics)
    {
        foreach (var link in links)
        {
            var context = CreateContext(link.Source);
            var candidate = CompileLink(
                new LinkCompilationInput(
                    link.Source,
                    link.Target,
                    link.ConditionExpression,
                    link.DeclarationSide,
                    link.MessageType,
                    link.CodeCondition, Delivery: link.Delivery, IsFallback: link.IsFallback),
                context,
                components,
                conditionCache,
                diagnostics);
            if (candidate is null)
                continue;

            candidates.Add(candidate);

            if (link.CodeCondition is null &&
                ApplicationLinkDeclarationProjection.CanProject(
                    link.Source,
                    link.Target,
                    link.DeclarationSide))
            {
                projections.Add(new ApplicationLinkDeclarationProjection(
                    link.Source,
                    link.Target,
                    link.ConditionExpression,
                    link.DeclarationSide, delivery: link.Delivery, isFallback: link.IsFallback));
            }
        }
    }

    private LinkCandidate? CompileLink(
        LinkCompilationInput link,
        DeclarationContext context,
        IReadOnlyDictionary<ComponentKey, RegisteredComponent> components,
        ApplicationConditionCompiler conditionCache,
        List<ApplicationLinkDiagnostic> diagnostics)
    {
        // Resolve both endpoints and compile the condition even when another check fails.
        var sourceValid = TryResolveMetadata(
            link.Source,
            output: true,
            components,
            _systemOutputs,
            context,
            diagnostics,
            out var sourceMetadata);
        var targetValid = TryResolveMetadata(
            link.Target,
            output: false,
            components,
            _systemOutputs,
            context,
            diagnostics,
            out var targetMetadata);
        IFlowCompiledExpression<bool>? compiledExpression = null;
        var conditionValid = link.ConditionExpression is null ||
            TryCompileCondition(
                link.ConditionExpression,
                link.Source, sourceMetadata?.MessageType ?? typeof(object), link.Parameters,
                context,
                conditionCache,
                diagnostics,
                out compiledExpression);

        if (!sourceValid || !targetValid || !conditionValid)
            return null;

        var messageType = sourceMetadata!.MessageType;
        if (link.DeclaredMessageType is not null &&
            messageType != typeof(FlowValue) &&
            messageType != link.DeclaredMessageType)
        {
            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.PortTypeMismatch,
                $"Link '{link.Source}' to '{link.Target}' declares '{link.DeclaredMessageType}' but source port '{link.Source}' carries '{messageType}'.",
                context,
                link.Source,
                link.Target));
            return null;
        }

        if (targetMetadata!.Kind != ComponentPortKind.Signal &&
            messageType != targetMetadata.MessageType)
        {
            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.PortTypeMismatch,
                $"Link '{link.Source}' to '{link.Target}' connects '{messageType}' to incompatible '{targetMetadata.MessageType}'.",
                context,
                link.Source,
                link.Target));
            return null;
        }

        var condition = link.CodeCondition;
        if (condition is null && compiledExpression is not null)
            condition = context => compiledExpression.Evaluate(context);

        return new LinkCandidate(
            new CompiledApplicationLink(
                link.Source,
                link.Target,
                link.DeclaredMessageType ?? messageType,
                link.ConditionExpression,
                condition,
                link.DeclarationSide, link.Delivery, link.IsFallback),
            context);
    }

    private static DeclarationContext CreateContext(ApplicationAddress source)
        => source.Kind == ApplicationAddressKind.WorkflowPort
            ? new DeclarationContext(source.Segments[0], source.Segments[1], source.Segments[2])
            : new DeclarationContext(null, null, source.Value);

    private Dictionary<ComponentKey, RegisteredComponent> IndexComponents(
        ApplicationDefinition definition,
        List<ApplicationLinkDiagnostic> diagnostics)
    {
        var components = new Dictionary<ComponentKey, RegisteredComponent>();

        foreach (var workflow in definition.Workflows.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var component in workflow.Value.Components.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var key = new ComponentKey(workflow.Key, component.Key);
                IReadOnlyDictionary<string, ComponentPortMetadata>? inputs = null, outputs = null;
                if (_instances.TryGetValue(key.Value, out var instance))
                    (inputs, outputs) = (instance.InputMetadata, instance.OutputMetadata);
                components.Add(key, new RegisteredComponent(component.Value, inputs, outputs));

                if (inputs is null)
                {
                    diagnostics.Add(CreateDiagnostic(
                        ApplicationLinkDiagnosticCode.UnknownComponentType,
                        $"Component '{key.Value}' uses unknown type '{component.Value.Type}'.",
                        new DeclarationContext(workflow.Key, component.Key, null)));
                }
            }
        }

        return components;
    }

    private bool TryCompileCondition(
        string expression,
        ApplicationAddress source,
        Type inputType,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        DeclarationContext context,
        ApplicationConditionCompiler cache,
        List<ApplicationLinkDiagnostic> diagnostics,
        out IFlowCompiledExpression<bool>? compiled)
    {
        compiled = null;
        try
        {
            compiled = cache.Compile(source, expression, inputType, parameters);
            return true;
        }
        catch (MissingConditionEngineException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.MissingConditionEngine,
                $"Conditional link '{context.Value}' requires an expression engine." +
                (exception.EngineName is null ? "" : $" {exception.Message}"),
                context));
            return false;
        }

        catch (Exception exception)
        {
            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.InvalidCondition,
                $"Conditional link '{context.Value}' has invalid expression '{expression}': {exception.Message}",
                context,
                exception: exception));
            return false;
        }

    }

    private static bool TryResolveMetadata(
        ApplicationAddress address,
        bool output,
        IReadOnlyDictionary<ComponentKey, RegisteredComponent> components,
        IReadOnlyDictionary<ApplicationAddress, ComponentPortMetadata> systemOutputs,
        DeclarationContext context,
        List<ApplicationLinkDiagnostic> diagnostics,
        out ComponentPortMetadata? metadata)
    {
        metadata = null;
        if (address.Kind == ApplicationAddressKind.SystemPort)
        {
            if (output)
            {
                if (systemOutputs.TryGetValue(address, out metadata))
                    return true;

                diagnostics.Add(CreateDiagnostic(
                    ApplicationLinkDiagnosticCode.MissingSystemOutputMetadata,
                    $"System output '{address}' has no registered message type metadata.",
                    context,
                    source: address));
                return false;
            }

            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.InvalidPortReference,
                $"System port '{address}' is output-only and cannot be a link target.",
                context,
                target: address));
            return false;
        }

        if (address.Kind != ApplicationAddressKind.WorkflowPort)
        {
            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.InvalidPortReference,
                $"Address '{address}' is not a workflow or system port.",
                context));
            return false;
        }

        var componentKey = new ComponentKey(address.Segments[0], address.Segments[1]);
        if (!components.TryGetValue(componentKey, out var component))
        {
            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.MissingComponent,
                $"Link references missing component '{componentKey.Value}'.",
                context,
                output ? address : null,
                output ? null : address));
            return false;
        }

        if (component.Inputs is null)
            return false;

        var ports = output ? component.Outputs! : component.Inputs!;
        if (!ports.TryGetValue(address.Segments[2], out metadata))
        {
            var code = output
                ? ApplicationLinkDiagnosticCode.MissingOutputPort
                : ApplicationLinkDiagnosticCode.MissingInputPort;
            var role = output ? "output" : "input";
            diagnostics.Add(CreateDiagnostic(
                code,
                $"Component '{componentKey.Value}' has no {role} port '{address.Segments[2]}'.",
                context,
                output ? address : null,
                output ? null : address));
            return false;
        }

        return true;
    }

    private static IReadOnlyDictionary<ApplicationAddress, ComponentPortMetadata> CopySystemOutputs(
        IEnumerable<ApplicationSystemOutputMetadata>? systemOutputs)
    {
        var result = new Dictionary<ApplicationAddress, ComponentPortMetadata>(
            ApplicationAddressComparer.Instance);
        if (systemOutputs is null)
            return result;

        foreach (var systemOutput in systemOutputs)
        {
            ArgumentNullException.ThrowIfNull(systemOutput);
            if (!result.TryAdd(
                    systemOutput.Address,
                    new ComponentPortMetadata(
                        systemOutput.Address.Segments[^1],
                        systemOutput.MessageType)))
            {
                throw new ArgumentException(
                    $"System output '{systemOutput.Address}' is registered more than once.",
                    nameof(systemOutputs));
            }
        }

        return result;
    }

    private static List<LinkCandidate> RemoveDuplicates(
        IEnumerable<LinkCandidate> candidates,
        List<ApplicationLinkDiagnostic> diagnostics)
    {
        var seen = new HashSet<LinkEndpoints>();
        var result = new List<LinkCandidate>();
        foreach (var candidate in candidates)
        {
            var endpoints = new LinkEndpoints(candidate.Link.Source.Value, candidate.Link.Target.Value);
            if (seen.Add(endpoints))
            {
                result.Add(candidate);
                continue;
            }

            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.DuplicateLink,
                $"Link '{candidate.Link.Source}' to '{candidate.Link.Target}' is declared more than once.",
                candidate.Context,
                candidate.Link.Source,
                candidate.Link.Target));
        }

        return result;
    }

    private static void ValidateExclusiveClaims(
        IReadOnlyList<LinkCandidate> links,
        IReadOnlyDictionary<ComponentKey, RegisteredComponent> components,
        List<ApplicationLinkDiagnostic> diagnostics)
    {
        ValidateExclusiveClaims(links, components, output: true, diagnostics);
        ValidateExclusiveClaims(links, components, output: false, diagnostics);
    }

    private static void ValidateExclusiveClaims(
        IReadOnlyList<LinkCandidate> links,
        IReadOnlyDictionary<ComponentKey, RegisteredComponent> components,
        bool output,
        List<ApplicationLinkDiagnostic> diagnostics)
    {
        var claims = links
            .GroupBy(
                candidate => output ? candidate.Link.Source : candidate.Link.Target,
                ApplicationAddressComparer.Instance)
            .OrderBy(static group => group.Key.Value, StringComparer.Ordinal);

        foreach (var claim in claims)
        {
            if (claim.Count() < 2 ||
                !TryFindMetadata(claim.Key, output, components, out var metadata) ||
                metadata!.LinkCardinality != ComponentPortLinkCardinality.Single)
            {
                continue;
            }

            var first = claim.First();
            var role = output ? "output" : "input";
            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.ExclusivePortClaim,
                $"{role} port '{claim.Key}' allows one link but has {claim.Count()} claims.",
                new DeclarationContext(claim.Key.Segments[0], claim.Key.Segments[1], claim.Key.Segments[2]),
                first.Link.Source,
                first.Link.Target));
        }
    }

    private static bool TryFindMetadata(
        ApplicationAddress address,
        bool output,
        IReadOnlyDictionary<ComponentKey, RegisteredComponent> components,
        out ComponentPortMetadata? metadata)
    {
        metadata = null;
        if (address.Kind != ApplicationAddressKind.WorkflowPort)
            return false;

        var key = new ComponentKey(address.Segments[0], address.Segments[1]);
        if (!components.TryGetValue(key, out var component) || component.Inputs is null)
            return false;

        return (output ? component.Outputs! : component.Inputs!)
            .TryGetValue(address.Segments[2], out metadata);
    }

    private static void ValidateAcyclic(
        IReadOnlyList<LinkCandidate> links,
        IReadOnlyDictionary<ComponentKey, RegisteredComponent> components,
        List<ApplicationLinkDiagnostic> diagnostics)
    {
        var edges = new Dictionary<ComponentKey, HashSet<ComponentKey>>();
        foreach (var candidate in links)
        {
            if (candidate.Link.Source.Kind != ApplicationAddressKind.WorkflowPort)
                continue;

            if (TryFindMetadata(candidate.Link.Target, output: false, components, out var targetMetadata) &&
                targetMetadata!.Kind == ComponentPortKind.Signal)
            {
                continue;
            }

            var source = ComponentKey.From(candidate.Link.Source);
            var target = ComponentKey.From(candidate.Link.Target);
            if (!edges.TryGetValue(source, out var targets))
            {
                targets = [];
                edges.Add(source, targets);
            }

            targets.Add(target);
            edges.TryAdd(target, []);
        }

        var index = 0;
        var indices = new Dictionary<ComponentKey, int>();
        var lowLinks = new Dictionary<ComponentKey, int>();
        var stack = new Stack<ComponentKey>();
        var onStack = new HashSet<ComponentKey>();
        var cycles = new List<ComponentKey[]>();

        void Visit(ComponentKey node)
        {
            indices[node] = index;
            lowLinks[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var target in edges[node].OrderBy(static key => key.Value, StringComparer.Ordinal))
            {
                if (!indices.ContainsKey(target))
                {
                    Visit(target);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[target]);
                }
                else if (onStack.Contains(target))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], indices[target]);
                }
            }

            if (lowLinks[node] != indices[node])
                return;

            var component = new List<ComponentKey>();
            ComponentKey current;
            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            }
            while (!current.Equals(node));

            component.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Value, right.Value));
            if (component.Count > 1 || edges[node].Contains(node))
                cycles.Add(component.ToArray());
        }

        foreach (var node in edges.Keys.OrderBy(static key => key.Value, StringComparer.Ordinal))
        {
            if (!indices.ContainsKey(node))
                Visit(node);
        }

        foreach (var cycle in cycles.OrderBy(static value => value[0].Value, StringComparer.Ordinal))
        {
            var members = cycle.ToHashSet();
            var edge = links.First(candidate =>
                candidate.Link.Source.Kind == ApplicationAddressKind.WorkflowPort &&
                members.Contains(ComponentKey.From(candidate.Link.Source)) &&
                members.Contains(ComponentKey.From(candidate.Link.Target)));
            var workflow = cycle.Select(static key => key.Workflow).Distinct(StringComparer.Ordinal).Count() == 1
                ? cycle[0].Workflow
                : null;
            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.CycleDetected,
                $"Data-link cycle detected among components {string.Join(", ", cycle.Select(static key => $"'{key.Value}'"))}.",
                new DeclarationContext(workflow, null, null),
                edge.Link.Source,
                edge.Link.Target));
        }
    }

    private static ApplicationLinkDiagnostic CreateDiagnostic(
        ApplicationLinkDiagnosticCode code,
        string message,
        DeclarationContext context,
        ApplicationAddress? source = null,
        ApplicationAddress? target = null,
        Exception? exception = null)
        => new()
        {
            Code = code,
            Message = message,
            WorkflowName = context.Workflow,
            ComponentName = context.Component,
            PropertyName = context.Property,
            Source = source,
            Target = target,
            Exception = exception
        };

    private sealed record RegisteredComponent(
        ComponentDefinition Definition,
        IReadOnlyDictionary<string, ComponentPortMetadata>? Inputs,
        IReadOnlyDictionary<string, ComponentPortMetadata>? Outputs);

    private readonly record struct LinkCompilationInput(
        ApplicationAddress Source,
        ApplicationAddress Target,
        string? ConditionExpression,
        ApplicationLinkDeclarationSide DeclarationSide,
        Type? DeclaredMessageType = null,
        Func<FlowMapContext, bool>? CodeCondition = null,
        IReadOnlyDictionary<string, JsonElement>? Parameters = null,
        FlowDeliveryPolicy Delivery = FlowDeliveryPolicy.Reliable,
        bool IsFallback = false);

    private sealed record LinkCandidate(
        CompiledApplicationLink Link,
        DeclarationContext Context);

    private readonly record struct LinkEndpoints(string Source, string Target);

    private readonly record struct DeclarationContext(
        string? Workflow,
        string? Component,
        string? Property)
    {
        public string Value => string.Join('.', new[] { Workflow, Component, Property }.Where(static value => value is not null));
    }

    private readonly record struct ComponentKey(string Workflow, string Component)
    {
        public string Value => $"{Workflow}.{Component}";

        public static ComponentKey From(ApplicationAddress address)
            => new(address.Segments[0], address.Segments[1]);
    }

    private sealed class ApplicationAddressComparer : IEqualityComparer<ApplicationAddress>
    {
        public static ApplicationAddressComparer Instance { get; } = new();

        public bool Equals(ApplicationAddress? left, ApplicationAddress? right) => left == right;

        public int GetHashCode(ApplicationAddress value) => value.GetHashCode();
    }
}
