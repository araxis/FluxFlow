using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;

namespace FluxFlow.Engine.Runtime;

public sealed class ApplicationRuntimeBuilder
{
    private readonly RuntimeNodeFactoryRegistry _factories;
    private readonly ApplicationDefinitionValidator _validator;
    private readonly IFlowExpressionEngine _linkConditionExpressionEngine;

    public ApplicationRuntimeBuilder(
        RuntimeNodeFactoryRegistry factories,
        ApplicationDefinitionValidator? validator = null,
        IFlowExpressionEngine? linkConditionExpressionEngine = null)
    {
        _factories = factories;
        _validator = validator ?? new ApplicationDefinitionValidator();
        _linkConditionExpressionEngine = linkConditionExpressionEngine ?? new DynamicExpressoFlowExpressionEngine();
    }

    public ApplicationRuntimeBuildResult Build(ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var validation = _validator.Validate(definition);
        if (!validation.IsValid)
        {
            return ApplicationRuntimeBuildResult.Failed(
                validation,
                validation.Errors
                    .Select(error => new ApplicationRuntimeBuildError(
                        ApplicationRuntimeBuildErrorCode.ValidationFailed,
                        error.Message,
                        error.WorkflowName,
                        ToNodeName(error.NodeName),
                        ToPortName(error.PortName)))
                    .ToArray());
        }

        var errors = new List<ApplicationRuntimeBuildError>();
        var resourceLinks = new List<IDisposable>();
        var workflowLinks = new Dictionary<string, List<IDisposable>>();
        var linkedTargets = new HashSet<RuntimeNode>();
        var linkedOutputs = new HashSet<OutputPort>();

        var resourceNodes = CreateNodes(null, definition.Resources, errors);
        var workflowNodes = definition.Workflows.ToDictionary(
            workflow => workflow.Key,
            workflow => (IReadOnlyDictionary<NodeName, RuntimeNode>)CreateNodes(workflow.Key, workflow.Value.Nodes, errors, resourceNodes));

        if (errors.Count == 0)
        {
            foreach (var key in workflowNodes.Keys)
                workflowLinks[key] = [];

            LinkWorkflows(definition, resourceNodes, workflowNodes, workflowLinks, linkedTargets, linkedOutputs, errors);
            if (errors.Count == 0)
            {
                DrainUnlinkedOutputs(resourceNodes.Values, workflowNodes, resourceLinks, workflowLinks, linkedOutputs);
            }
        }

        if (errors.Count > 0)
        {
            DisposeCreatedNodes(
                resourceNodes,
                workflowNodes,
                resourceLinks.Concat(workflowLinks.Values.SelectMany(l => l)).ToList(),
                errors);
            return ApplicationRuntimeBuildResult.Failed(validation, errors);
        }

        var resources = resourceNodes.Values.ToArray();
        var workflows = workflowNodes
            .Select(kvp =>
            {
                var nodes = kvp.Value.Values.ToArray();
                var entryNodes = nodes.Where(n => !linkedTargets.Contains(n)).ToArray();
                return new Workflow(new WorkflowName(kvp.Key), nodes, workflowLinks[kvp.Key], entryNodes);
            })
            .ToArray();
        var resourceEntryNodes = resources.Where(n => !linkedTargets.Contains(n)).ToArray();

        return ApplicationRuntimeBuildResult.Succeeded(
            new ApplicationRuntime(resources, workflows, resourceEntryNodes, resourceLinks),
            validation);
    }

    private IReadOnlyDictionary<NodeName, RuntimeNode> CreateNodes(
        string? workflowName,
        IReadOnlyDictionary<string, NodeDefinition> definitions,
        List<ApplicationRuntimeBuildError> errors,
        IReadOnlyDictionary<NodeName, RuntimeNode>? resources = null)
    {
        var nodes = new Dictionary<NodeName, RuntimeNode>();
        var resourceView = resources ?? nodes;

        foreach (var definition in definitions)
        {
            var nodeName = new NodeName(definition.Key);

            if (!_factories.TryGetFactory(definition.Value.Type, out var factory))
            {
                errors.Add(new(
                    ApplicationRuntimeBuildErrorCode.UnknownNodeType,
                    $"No flow node factory is registered for type '{definition.Value.Type}'.",
                    workflowName,
                    nodeName));
                continue;
            }

            try
            {
                var runtimeNode = factory(new RuntimeNodeFactoryContext(
                    nodeName,
                    definition.Value,
                    workflowName,
                    resourceView));
                nodes.Add(nodeName, runtimeNode with
                {
                    Phase = definition.Value.Phase,
                    Type = definition.Value.Type
                });
            }
            catch (Exception exception)
            {
                errors.Add(new(
                    ApplicationRuntimeBuildErrorCode.FactoryFailed,
                    $"Factory for node '{nodeName}' failed: {exception.Message}",
                    workflowName,
                    nodeName));
            }
        }

        return nodes;
    }

    private void LinkWorkflows(
        ApplicationDefinition definition,
        IReadOnlyDictionary<NodeName, RuntimeNode> resources,
        IReadOnlyDictionary<string, IReadOnlyDictionary<NodeName, RuntimeNode>> workflows,
        Dictionary<string, List<IDisposable>> workflowLinks,
        HashSet<RuntimeNode> linkedTargets,
        HashSet<OutputPort> allLinkedOutputs,
        List<ApplicationRuntimeBuildError> errors)
    {
        foreach (var workflowDefinition in definition.Workflows)
        {
            var workflowName = workflowDefinition.Key;
            var workflowNodes = workflows[workflowName];
            var links = workflowLinks[workflowName];

            foreach (var targetDefinition in workflowDefinition.Value.Nodes)
            {
                var targetName = new NodeName(targetDefinition.Key);
                var targetNode = workflowNodes[targetName];

                foreach (var portLinks in targetDefinition.Value.GetAllPortLinks(workflowName))
                {
                    var targetPortName = new PortName(portLinks.Key);
                    var input = targetNode.FindInput(targetPortName);
                    if (input is null)
                    {
                        errors.Add(new(
                            ApplicationRuntimeBuildErrorCode.MissingInputPort,
                            $"Node '{targetName}' does not expose input port '{targetPortName}'.",
                            workflowName,
                            targetName,
                            targetPortName));
                        continue;
                    }

                    var resolvedLinks = new List<ResolvedOutputLink>();
                    foreach (var link in portLinks.Value)
                    {
                        if (!TryFindSource(link.From, workflows, resources, out var sourceNode))
                        {
                            continue;
                        }

                        var output = sourceNode.FindOutput(link.From.Port);
                        if (output is null)
                        {
                            errors.Add(new(
                                ApplicationRuntimeBuildErrorCode.MissingOutputPort,
                                $"Node '{sourceNode.Address}' does not expose output port '{link.From.Port}'.",
                                workflowName,
                                sourceNode.Address.Node,
                                link.From.Port));
                            continue;
                        }

                        resolvedLinks.Add(new ResolvedOutputLink(output, CreateLinkCondition(link)));
                    }

                    var shouldCoordinateCompletion = resolvedLinks.Count > 1;
                    var linkedOutputs = new List<OutputPort>();
                    foreach (var resolvedLink in resolvedLinks)
                    {
                        var disposable = resolvedLink.Output.TryLinkTo(
                            input,
                            propagateCompletion: !shouldCoordinateCompletion,
                            resolvedLink.Condition,
                            out var error);
                        if (error is not null)
                        {
                            errors.Add(error with
                            {
                                WorkflowName = workflowName,
                                NodeName = targetName,
                                PortName = targetPortName
                            });
                            continue;
                        }

                        if (disposable is not null)
                        {
                            links.Add(disposable);
                            linkedTargets.Add(targetNode);
                            linkedOutputs.Add(resolvedLink.Output);
                            allLinkedOutputs.Add(resolvedLink.Output);
                        }
                    }

                    if (shouldCoordinateCompletion && linkedOutputs.Count == resolvedLinks.Count)
                    {
                        links.Add(new InputCompletionLink(input, linkedOutputs));
                    }
                }
            }
        }
    }

    private IFlowPredicate<object?>? CreateLinkCondition(LinkDefinition link)
        => string.IsNullOrWhiteSpace(link.When)
            ? null
            : new ExpressionFlowPredicate<object?>(link.When, _linkConditionExpressionEngine);

    private static void DrainUnlinkedOutputs(
        IEnumerable<RuntimeNode> resources,
        IReadOnlyDictionary<string, IReadOnlyDictionary<NodeName, RuntimeNode>> workflows,
        List<IDisposable> resourceLinks,
        Dictionary<string, List<IDisposable>> workflowLinks,
        HashSet<OutputPort> linkedOutputs)
    {
        AddUnlinkedOutputDrains(resources, resourceLinks, linkedOutputs);

        foreach (var workflow in workflows)
        {
            AddUnlinkedOutputDrains(workflow.Value.Values, workflowLinks[workflow.Key], linkedOutputs);
        }
    }

    private static void AddUnlinkedOutputDrains(
        IEnumerable<RuntimeNode> nodes,
        List<IDisposable> links,
        HashSet<OutputPort> linkedOutputs)
    {
        foreach (var output in nodes.SelectMany(node => node.Outputs))
        {
            if (!output.DrainWhenUnlinked || linkedOutputs.Contains(output))
            {
                continue;
            }

            links.Add(output.LinkToDiscard());
        }
    }

    private static bool TryFindSource(
        PortAddress source,
        IReadOnlyDictionary<string, IReadOnlyDictionary<NodeName, RuntimeNode>> workflows,
        IReadOnlyDictionary<NodeName, RuntimeNode> resources,
        out RuntimeNode sourceNode)
    {
        IReadOnlyDictionary<NodeName, RuntimeNode>? scope = source.Scope == WellKnownScopes.Resources
            ? resources
            : workflows.GetValueOrDefault(source.Scope);

        if (scope is null)
        {
            sourceNode = null!;
            return false;
        }

        return scope.TryGetValue(source.Node, out sourceNode!);
    }

    private static void DisposeCreatedNodes(
        IReadOnlyDictionary<NodeName, RuntimeNode> resources,
        IReadOnlyDictionary<string, IReadOnlyDictionary<NodeName, RuntimeNode>> workflows,
        List<IDisposable> links,
        List<ApplicationRuntimeBuildError> buildErrors)
    {
        var cleanupErrors = new List<Exception>();
        foreach (var link in links)
            RuntimeCleanup.TryDisposeLink(link, cleanupErrors, "Runtime build");

        foreach (var output in workflows.Values.SelectMany(wf => wf.Values).SelectMany(node => node.Outputs))
            RuntimeCleanup.TryDisposeOutput(output, cleanupErrors, "Runtime build");

        foreach (var node in workflows.Values.SelectMany(wf => wf.Values))
            RuntimeCleanup.TryDisposeNode(node, cleanupErrors, "Runtime build");

        foreach (var output in resources.Values.SelectMany(node => node.Outputs))
            RuntimeCleanup.TryDisposeOutput(output, cleanupErrors, "Runtime build");

        foreach (var resource in resources.Values)
            RuntimeCleanup.TryDisposeNode(resource, cleanupErrors, "Runtime build");

        if (cleanupErrors.Count > 0)
        {
            buildErrors.Add(new(
                ApplicationRuntimeBuildErrorCode.CleanupFailed,
                $"One or more resources failed while cleaning up a failed runtime build: {new AggregateException(cleanupErrors).Message}"));
        }
    }

    private static NodeName? ToNodeName(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new NodeName(value);

    private static PortName? ToPortName(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new PortName(value);

    private sealed record ResolvedOutputLink(
        OutputPort Output,
        IFlowPredicate<object?>? Condition);

    private sealed class InputCompletionLink : IDisposable
    {
        private readonly CancellationTokenSource _disposed = new();
        private readonly Task[] _watchers;
        private int _remaining;
        private int _finished;
        private int _disposeStarted;

        public InputCompletionLink(InputPort input, IReadOnlyCollection<OutputPort> outputs)
        {
            _remaining = outputs.Count;
            _watchers = outputs
                .Select(output => WatchSourceCompletionAsync(input, output.Completion, _disposed.Token))
                .ToArray();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            {
                return;
            }

            _disposed.Cancel();
            _ = DisposeTokenWhenWatchersStopAsync();
        }

        private async Task DisposeTokenWhenWatchersStopAsync()
        {
            try
            {
                await Task.WhenAll(_watchers).ConfigureAwait(false);
            }
            catch
            {
                // Watchers only observe completion so the token source can be released.
            }
            finally
            {
                _disposed.Dispose();
            }
        }

        private async Task WatchSourceCompletionAsync(
            InputPort input,
            Task sourceCompletion,
            CancellationToken cancellationToken)
        {
            try
            {
                await sourceCompletion.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref _disposeStarted) != 0)
                {
                    return;
                }

                if (Interlocked.Exchange(ref _finished, 1) == 0)
                {
                    input.Fault(exception);
                }

                return;
            }

            if (Interlocked.Decrement(ref _remaining) == 0 &&
                Interlocked.Exchange(ref _finished, 1) == 0)
            {
                input.Complete();
            }
        }
    }
}
