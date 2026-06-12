namespace FluxFlow.Engine.Definitions;

public sealed class ApplicationDefinitionValidator
{
    public ApplicationDefinitionValidationResult Validate(ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<ApplicationDefinitionValidationError>();

        if (definition.Workflows.Count == 0)
        {
            errors.Add(new(
                ApplicationDefinitionValidationErrorCode.EmptyDefinition,
                "Flow application definition must contain at least one workflow."));
        }

        foreach (var resource in definition.Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Key))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.EmptyResourceName,
                    "Resource name cannot be empty."));
            }
            else if (resource.Key.Contains('.'))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.InvalidResourceName,
                    $"Resource name '{resource.Key}' cannot contain '.'."));
            }

            ValidateNode(null, resource.Key, resource.Value, errors);
        }

        foreach (var workflow in definition.Workflows)
        {
            if (string.IsNullOrWhiteSpace(workflow.Key))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.EmptyWorkflowName,
                    "Workflow name cannot be empty."));
            }
            else if (workflow.Key.Contains('.'))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.InvalidWorkflowName,
                    $"Workflow name '{workflow.Key}' cannot contain '.'.",
                    workflow.Key));
            }
            else if (workflow.Key == WellKnownScopes.Resources)
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.InvalidWorkflowName,
                    $"Workflow name '{workflow.Key}' is reserved for the resource scope.",
                    workflow.Key));
            }

            if (workflow.Value.Nodes.Count == 0)
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.EmptyWorkflow,
                    $"Workflow '{workflow.Key}' must contain at least one node."));
            }

            foreach (var node in workflow.Value.Nodes)
            {
                ValidateNode(workflow.Key, node.Key, node.Value, errors);
            }

            ValidateLinks(workflow.Key, workflow.Value.Nodes, definition, errors);
        }

        if (errors.Count == 0)
        {
            ValidateAcyclic(definition, errors);
        }

        return new ApplicationDefinitionValidationResult(errors);
    }

    private static void ValidateNode(
        string? workflowName,
        string nodeName,
        NodeDefinition node,
        List<ApplicationDefinitionValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            errors.Add(new(
                ApplicationDefinitionValidationErrorCode.EmptyNodeName,
                "Flow node name cannot be empty.",
                workflowName,
                nodeName));
        }
        else if (nodeName.Contains('.'))
        {
            errors.Add(new(
                ApplicationDefinitionValidationErrorCode.InvalidNodeName,
                $"Flow node name '{nodeName}' cannot contain '.'.",
                workflowName,
                nodeName));
        }

        if (string.IsNullOrWhiteSpace(node.Type.Value))
        {
            errors.Add(new(
                ApplicationDefinitionValidationErrorCode.EmptyNodeType,
                $"Flow node '{nodeName}' has an empty node type.",
                workflowName,
                nodeName));
        }
    }

    private static void ValidateLinks(
        string workflowName,
        IReadOnlyDictionary<string, NodeDefinition> nodes,
        ApplicationDefinition definition,
        List<ApplicationDefinitionValidationError> errors)
    {
        var knownLinks = new HashSet<LinkKey>();

        foreach (var targetNode in nodes)
        {
            foreach (var port in targetNode.Value.Ports)
            {
                if (string.IsNullOrWhiteSpace(port.Key))
                {
                    errors.Add(new(
                        ApplicationDefinitionValidationErrorCode.EmptyTargetPort,
                        $"Node '{targetNode.Key}' in workflow '{workflowName}' has an empty target port.",
                        workflowName,
                        targetNode.Key));
                }

                IReadOnlyList<LinkDefinition> links;

                try
                {
                    links = targetNode.Value.GetPortLinks(port.Key, workflowName);
                }
                catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException or ArgumentException)
                {
                    errors.Add(new(
                        ApplicationDefinitionValidationErrorCode.InvalidLink,
                        $"Node '{targetNode.Key}' port '{port.Key}' in workflow '{workflowName}' has an invalid link: {exception.Message}",
                        workflowName,
                        targetNode.Key,
                        port.Key));
                    continue;
                }

                foreach (var link in links)
                {
                    if (string.IsNullOrWhiteSpace(link.From.Port.Value))
                    {
                        errors.Add(new(
                            ApplicationDefinitionValidationErrorCode.EmptySourcePort,
                            $"Node '{targetNode.Key}' port '{port.Key}' in workflow '{workflowName}' has an empty source port.",
                            workflowName,
                            targetNode.Key,
                            port.Key));
                    }

                    ValidateSourceNode(targetNode.Key, port.Key, workflowName, link.From, definition, errors);

                    if (!knownLinks.Add(new LinkKey(targetNode.Key, port.Key, link.From, link.When)))
                    {
                        errors.Add(new(
                            ApplicationDefinitionValidationErrorCode.DuplicateLink,
                            $"Node '{targetNode.Key}' port '{port.Key}' in workflow '{workflowName}' has a duplicate link from '{link.From}'.",
                            workflowName,
                            targetNode.Key,
                            port.Key));
                    }
                }
            }
        }
    }

    private static void ValidateSourceNode(
        string targetNodeName,
        string targetPortName,
        string targetWorkflowName,
        PortAddress source,
        ApplicationDefinition definition,
        List<ApplicationDefinitionValidationError> errors)
    {
        if (source.Scope == WellKnownScopes.Resources)
        {
            if (!definition.Resources.ContainsKey(source.Node.Value))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.MissingSourceNode,
                    $"Node '{targetNodeName}' port '{targetPortName}' in workflow '{targetWorkflowName}' references missing resource '{source.Node}'.",
                    targetWorkflowName,
                    targetNodeName,
                    targetPortName));
            }

            return;
        }

        if (!definition.Workflows.TryGetValue(source.Scope, out var sourceWorkflow))
        {
            errors.Add(new(
                ApplicationDefinitionValidationErrorCode.MissingSourceNode,
                $"Node '{targetNodeName}' port '{targetPortName}' in workflow '{targetWorkflowName}' references unknown workflow scope '{source.Scope}'.",
                targetWorkflowName,
                targetNodeName,
                targetPortName));
            return;
        }

        if (!sourceWorkflow.Nodes.ContainsKey(source.Node.Value))
        {
            errors.Add(new(
                ApplicationDefinitionValidationErrorCode.MissingSourceNode,
                $"Node '{targetNodeName}' port '{targetPortName}' in workflow '{targetWorkflowName}' references missing node '{source.Node}' in workflow '{source.Scope}'.",
                targetWorkflowName,
                targetNodeName,
                targetPortName));
        }
    }

    private static void ValidateAcyclic(
        ApplicationDefinition definition,
        List<ApplicationDefinitionValidationError> errors)
    {
        // Edges run from each link source node to its target node across all
        // workflows. Cycles can never complete gracefully (every node waits on
        // an upstream that transitively waits on it), so they are rejected.
        var edges = new Dictionary<(string Scope, string Node), List<(string Scope, string Node)>>();

        foreach (var workflow in definition.Workflows)
        {
            foreach (var node in workflow.Value.Nodes)
            {
                foreach (var port in node.Value.Ports)
                {
                    IReadOnlyList<LinkDefinition> links;
                    try
                    {
                        links = node.Value.GetPortLinks(port.Key, workflow.Key);
                    }
                    catch (Exception exception) when (
                        exception is FormatException or System.Text.Json.JsonException or ArgumentException)
                    {
                        continue;
                    }

                    foreach (var link in links)
                    {
                        if (link.From.Scope == WellKnownScopes.Resources)
                        {
                            continue;
                        }

                        var source = (link.From.Scope, link.From.Node.Value);
                        if (!edges.TryGetValue(source, out var targets))
                        {
                            targets = [];
                            edges[source] = targets;
                        }

                        targets.Add((workflow.Key, node.Key));
                    }
                }
            }
        }

        var states = new Dictionary<(string Scope, string Node), int>();
        foreach (var start in edges.Keys)
        {
            if (TryFindCycle(start, edges, states, [], out var cycle))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.CyclicLink,
                    $"Flow application definition contains a cyclic link path: {cycle}.",
                    cycle.Split('.')[0]));
                return;
            }
        }
    }

    private static bool TryFindCycle(
        (string Scope, string Node) start,
        IReadOnlyDictionary<(string Scope, string Node), List<(string Scope, string Node)>> edges,
        Dictionary<(string Scope, string Node), int> states,
        List<(string Scope, string Node)> path,
        out string cycle)
    {
        cycle = string.Empty;
        if (states.TryGetValue(start, out var state))
        {
            if (state == 2)
            {
                return false;
            }

            var cycleStart = path.IndexOf(start);
            var nodes = path.Skip(Math.Max(cycleStart, 0)).Append(start);
            cycle = string.Join(" -> ", nodes.Select(n => $"{n.Scope}.{n.Node}"));
            return true;
        }

        if (!edges.TryGetValue(start, out var targets))
        {
            states[start] = 2;
            return false;
        }

        states[start] = 1;
        path.Add(start);
        foreach (var target in targets)
        {
            if (TryFindCycle(target, edges, states, path, out cycle))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        states[start] = 2;
        return false;
    }

    private sealed record LinkKey(
        string TargetNode,
        string TargetPort,
        PortAddress Source,
        string? When);
}
