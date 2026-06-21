namespace FluxFlow.Composition;

public sealed class CompositionValidator
{
    public CompositionValidationResult Validate(
        CompositionDefinition definition,
        CompositionNodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(registry);

        var diagnostics = new List<CompositionDiagnostic>();
        if (definition.Workflows.Count == 0)
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.EmptyDefinition,
                Message = "Composition contains no workflows."
            });
            return new CompositionValidationResult(diagnostics);
        }

        foreach (var (workflowName, workflow) in definition.Workflows)
        {
            ValidateWorkflowName(workflowName, diagnostics);
            ValidateWorkflow(workflowName, workflow, definition, registry, diagnostics);
        }

        return new CompositionValidationResult(diagnostics);
    }

    private static void ValidateWorkflowName(
        string workflowName,
        List<CompositionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(workflowName))
            return;

        diagnostics.Add(new CompositionDiagnostic
        {
            Code = CompositionDiagnosticCode.EmptyWorkflowName,
            Message = "Workflow names cannot be empty."
        });
    }

    private static void ValidateWorkflow(
        string workflowName,
        WorkflowDefinition workflow,
        CompositionDefinition definition,
        CompositionNodeRegistry registry,
        List<CompositionDiagnostic> diagnostics)
    {
        if (workflow.Nodes.Count == 0)
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.EmptyWorkflow,
                WorkflowName = workflowName,
                Message = $"Workflow '{workflowName}' contains no nodes."
            });
        }

        foreach (var (nodeName, node) in workflow.Nodes)
        {
            ValidateNode(workflowName, nodeName, node, registry, diagnostics);
        }

        var seenLinks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in workflow.Links)
        {
            ValidateLink(workflowName, link, definition, registry, diagnostics, seenLinks);
        }
    }

    private static void ValidateNode(
        string workflowName,
        string nodeName,
        NodeDefinition node,
        CompositionNodeRegistry registry,
        List<CompositionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.EmptyNodeName,
                WorkflowName = workflowName,
                Message = $"Workflow '{workflowName}' contains an empty node name."
            });
        }

        if (string.IsNullOrWhiteSpace(node.Type))
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.EmptyNodeType,
                WorkflowName = workflowName,
                NodeName = nodeName,
                Message = $"Node '{workflowName}.{nodeName}' has no type."
            });
            return;
        }

        if (!registry.TryGetRegistration(node.Type, out _))
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.UnknownNodeType,
                WorkflowName = workflowName,
                NodeName = nodeName,
                Message = $"Node '{workflowName}.{nodeName}' uses unknown type '{node.Type}'."
            });
        }
    }

    private static void ValidateLink(
        string workflowName,
        LinkDefinition link,
        CompositionDefinition definition,
        CompositionNodeRegistry registry,
        List<CompositionDiagnostic> diagnostics,
        HashSet<string> seenLinks)
    {
        var from = link.From.ResolveWorkflow(workflowName);
        var to = link.To.ResolveWorkflow(workflowName);
        var linkKey = $"{from}->{to}";
        if (!seenLinks.Add(linkKey))
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.DuplicateLink,
                WorkflowName = workflowName,
                Link = link,
                Message = $"Duplicate link '{from} -> {to}'."
            });
        }

        var sourceNode = FindNode(definition, from);
        var targetNode = FindNode(definition, to);

        if (sourceNode is null)
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.MissingNode,
                WorkflowName = workflowName,
                Link = link,
                Message = $"Link source node '{from.Workflow}.{from.Node}' does not exist."
            });
        }

        if (targetNode is null)
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.MissingNode,
                WorkflowName = workflowName,
                Link = link,
                Message = $"Link target node '{to.Workflow}.{to.Node}' does not exist."
            });
        }

        if (sourceNode is null || targetNode is null)
            return;

        if (!registry.TryGetRegistration(sourceNode.Type, out var sourceRegistration)
            || !registry.TryGetRegistration(targetNode.Type, out var targetRegistration))
        {
            return;
        }

        var hasOutputMetadata = sourceRegistration.Outputs.Count > 0;
        var hasInputMetadata = targetRegistration.Inputs.Count > 0;

        if (hasOutputMetadata && !sourceRegistration.Outputs.TryGetValue(from.Port, out var output))
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.MissingOutputPort,
                WorkflowName = workflowName,
                Link = link,
                Message = $"Node '{from.Workflow}.{from.Node}' does not expose output port '{from.Port}'."
            });
        }

        if (hasInputMetadata && !targetRegistration.Inputs.TryGetValue(to.Port, out var input))
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.MissingInputPort,
                WorkflowName = workflowName,
                Link = link,
                Message = $"Node '{to.Workflow}.{to.Node}' does not expose input port '{to.Port}'."
            });
        }

        if (sourceRegistration.Outputs.TryGetValue(from.Port, out output)
            && targetRegistration.Inputs.TryGetValue(to.Port, out input)
            && output.MessageType != input.MessageType)
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.PortTypeMismatch,
                WorkflowName = workflowName,
                Link = link,
                Message =
                    $"Link '{from} -> {to}' connects {output.MessageType.Name} to {input.MessageType.Name}."
            });
        }
    }

    private static NodeDefinition? FindNode(CompositionDefinition definition, PortReference reference)
    {
        if (reference.Workflow is null)
            return null;

        return definition.Workflows.TryGetValue(reference.Workflow, out var workflow)
            && workflow.Nodes.TryGetValue(reference.Node, out var node)
                ? node
                : null;
    }
}
