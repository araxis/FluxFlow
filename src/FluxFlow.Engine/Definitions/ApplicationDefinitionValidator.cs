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

    private sealed record LinkKey(
        string TargetNode,
        string TargetPort,
        PortAddress Source,
        string? When);
}
