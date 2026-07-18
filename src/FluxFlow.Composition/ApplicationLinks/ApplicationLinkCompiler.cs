using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;
using FluxFlow.Mapping;

namespace FluxFlow.Composition.Links;

public sealed class ApplicationLinkCompiler
{
    private readonly CompositionNodeRegistry _registry;
    private readonly IFlowExpressionEngine? _conditionEngine;
    private readonly IReadOnlyDictionary<ApplicationAddress, CompositionPortMetadata> _systemOutputs;

    public ApplicationLinkCompiler(
        CompositionNodeRegistry registry,
        IFlowExpressionEngine? conditionEngine = null,
        IEnumerable<ApplicationSystemOutputMetadata>? systemOutputs = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _conditionEngine = conditionEngine;
        _systemOutputs = CopySystemOutputs(systemOutputs);
    }

    public ApplicationLinkCompilationResult Compile(ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var diagnostics = new List<ApplicationLinkDiagnostic>();
        var components = IndexComponents(definition, diagnostics);
        var conditionCache = new Dictionary<string, ConditionCompilation>(StringComparer.Ordinal);
        var candidates = new List<LinkCandidate>();

        foreach (var (componentKey, component) in components.OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal))
        {
            var registration = component.Registration;
            if (registration is null)
                continue;

            foreach (var property in component.Definition.Properties.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var isInput = registration.Inputs.ContainsKey(property.Key);
                var isOutput = registration.Outputs.ContainsKey(property.Key);
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

                    var sourceValid = TryResolveMetadata(
                        source,
                        output: true,
                        components,
                        _systemOutputs,
                        context,
                        diagnostics,
                        out var sourceMetadata);
                    var targetValid = TryResolveMetadata(
                        target,
                        output: false,
                        components,
                        _systemOutputs,
                        context,
                        diagnostics,
                        out var targetMetadata);
                    IFlowCompiledExpression<bool>? compiledCondition = null;
                    var conditionValid = declaration.Condition is null ||
                        TryCompileCondition(
                            declaration.Condition,
                            context,
                            conditionCache,
                            diagnostics,
                            out compiledCondition);

                    if (!sourceValid || !targetValid || !conditionValid)
                        continue;

                    if (sourceMetadata is not null &&
                        targetMetadata!.Kind != CompositionPortKind.Signal &&
                        sourceMetadata.MessageType != targetMetadata.MessageType)
                    {
                        diagnostics.Add(CreateDiagnostic(
                            ApplicationLinkDiagnosticCode.PortTypeMismatch,
                            $"Link '{source}' to '{target}' connects '{sourceMetadata.MessageType}' to incompatible '{targetMetadata.MessageType}'.",
                            context,
                            source,
                            target));
                        continue;
                    }

                    candidates.Add(new LinkCandidate(
                        new CompiledApplicationLink(
                            source,
                            target,
                            sourceMetadata!.MessageType,
                            declaration.Condition,
                            compiledCondition,
                            side),
                        context));
                }
            }
        }

        var normalized = RemoveDuplicates(candidates, diagnostics);
        ValidateExclusiveClaims(normalized, components, diagnostics);
        ValidateAcyclic(normalized, diagnostics);

        var links = normalized
            .Select(static candidate => candidate.Link)
            .OrderBy(static link => link.Source.Value, StringComparer.Ordinal)
            .ThenBy(static link => link.Target.Value, StringComparer.Ordinal)
            .ThenBy(static link => link.ConditionExpression, StringComparer.Ordinal)
            .ThenBy(static link => link.DeclarationSide)
            .ToArray();

        return new ApplicationLinkCompilationResult(links, diagnostics);
    }

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
                _registry.TryGetRegistration(component.Value.Type, out var registration);
                components.Add(key, new RegisteredComponent(component.Value, registration));

                if (registration is null)
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
        DeclarationContext context,
        Dictionary<string, ConditionCompilation> cache,
        List<ApplicationLinkDiagnostic> diagnostics,
        out IFlowCompiledExpression<bool>? compiled)
    {
        compiled = null;
        if (_conditionEngine is null)
        {
            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.MissingConditionEngine,
                $"Conditional link '{context.Value}' requires an expression engine.",
                context));
            return false;
        }

        if (!cache.TryGetValue(expression, out var compilation))
        {
            try
            {
                var value = _conditionEngine.Compile<bool>(expression);
                compilation = value is null
                    ? new ConditionCompilation(
                        null,
                        new InvalidOperationException(
                            $"Expression engine '{_conditionEngine.Name}' returned no compiled condition."))
                    : new ConditionCompilation(value, null);
            }
            catch (Exception exception)
            {
                compilation = new ConditionCompilation(null, exception);
            }

            cache.Add(expression, compilation);
        }

        if (compilation.Exception is not null)
        {
            diagnostics.Add(CreateDiagnostic(
                ApplicationLinkDiagnosticCode.InvalidCondition,
                $"Conditional link '{context.Value}' has invalid expression '{expression}': {compilation.Exception.Message}",
                context,
                exception: compilation.Exception));
            return false;
        }

        compiled = compilation.Compiled;
        return true;
    }

    private static bool TryResolveMetadata(
        ApplicationAddress address,
        bool output,
        IReadOnlyDictionary<ComponentKey, RegisteredComponent> components,
        IReadOnlyDictionary<ApplicationAddress, CompositionPortMetadata> systemOutputs,
        DeclarationContext context,
        List<ApplicationLinkDiagnostic> diagnostics,
        out CompositionPortMetadata? metadata)
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

        if (component.Registration is null)
            return false;

        var ports = output ? component.Registration.Outputs : component.Registration.Inputs;
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

    private static IReadOnlyDictionary<ApplicationAddress, CompositionPortMetadata> CopySystemOutputs(
        IEnumerable<ApplicationSystemOutputMetadata>? systemOutputs)
    {
        var result = new Dictionary<ApplicationAddress, CompositionPortMetadata>(
            ApplicationAddressComparer.Instance);
        if (systemOutputs is null)
            return result;

        foreach (var systemOutput in systemOutputs)
        {
            ArgumentNullException.ThrowIfNull(systemOutput);
            if (!result.TryAdd(
                    systemOutput.Address,
                    new CompositionPortMetadata(
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
                metadata!.LinkCardinality != CompositionPortLinkCardinality.Single)
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
        out CompositionPortMetadata? metadata)
    {
        metadata = null;
        if (address.Kind != ApplicationAddressKind.WorkflowPort)
            return false;

        var key = new ComponentKey(address.Segments[0], address.Segments[1]);
        if (!components.TryGetValue(key, out var component) || component.Registration is null)
            return false;

        return (output ? component.Registration.Outputs : component.Registration.Inputs)
            .TryGetValue(address.Segments[2], out metadata);
    }

    private static void ValidateAcyclic(
        IReadOnlyList<LinkCandidate> links,
        List<ApplicationLinkDiagnostic> diagnostics)
    {
        var edges = new Dictionary<ComponentKey, HashSet<ComponentKey>>();
        foreach (var candidate in links)
        {
            if (candidate.Link.Source.Kind != ApplicationAddressKind.WorkflowPort)
                continue;

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
                $"Link cycle detected among components {string.Join(", ", cycle.Select(static key => $"'{key.Value}'"))}.",
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
        CompositionNodeRegistration? Registration);

    private sealed record ConditionCompilation(
        IFlowCompiledExpression<bool>? Compiled,
        Exception? Exception);

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
