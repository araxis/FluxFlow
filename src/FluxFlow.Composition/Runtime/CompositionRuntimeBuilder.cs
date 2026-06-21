namespace FluxFlow.Composition;

public sealed class CompositionRuntimeBuilder
{
    private readonly CompositionNodeRegistry _registry;
    private readonly CompositionValidator _validator;

    public CompositionRuntimeBuilder(
        CompositionNodeRegistry registry,
        CompositionValidator? validator = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _validator = validator ?? new CompositionValidator();
    }

    public async ValueTask<CompositionBuildResult> BuildAsync(
        CompositionDefinition definition,
        IServiceProvider? services = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        services ??= EmptyServiceProvider.Instance;

        var diagnostics = _validator.Validate(definition, _registry).Diagnostics.ToList();
        if (diagnostics.Count > 0)
            return CompositionBuildResult.Failure(diagnostics);

        var nodes = new Dictionary<RuntimeNodeKey, CompositionRuntimeNode>();
        var links = new List<IDisposable>();
        var nodesWithIncomingLinks = new HashSet<RuntimeNodeKey>();

        foreach (var (workflowName, workflow) in definition.Workflows)
        {
            foreach (var (nodeName, nodeDefinition) in workflow.Nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = new RuntimeNodeKey(workflowName, nodeName);
                var registration = _registry.Registrations[nodeDefinition.Type];

                ComposedNode descriptor;
                try
                {
                    var context = new CompositionNodeFactoryContext(
                        services,
                        workflowName,
                        nodeName,
                        nodeDefinition);

                    descriptor = await registration.Factory(context).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new CompositionDiagnostic
                    {
                        Code = CompositionDiagnosticCode.FactoryFailed,
                        WorkflowName = workflowName,
                        NodeName = nodeName,
                        Exception = exception,
                        Message = $"Factory for node '{key}' failed: {exception.Message}"
                    });
                    continue;
                }

                if (descriptor is null)
                {
                    diagnostics.Add(new CompositionDiagnostic
                    {
                        Code = CompositionDiagnosticCode.FactoryFailed,
                        WorkflowName = workflowName,
                        NodeName = nodeName,
                        Message = $"Factory for node '{key}' returned null."
                    });
                    continue;
                }

                ValidateDescriptorPorts(key, registration, descriptor, diagnostics);
                nodes.Add(key, new CompositionRuntimeNode(key, nodeDefinition, descriptor));
            }
        }

        if (diagnostics.Count > 0)
        {
            await CleanupAsync(nodes.Values, links, diagnostics).ConfigureAwait(false);
            return CompositionBuildResult.Failure(diagnostics);
        }

        foreach (var (workflowName, workflow) in definition.Workflows)
        {
            foreach (var linkDefinition in workflow.Links)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LinkNodes(workflowName, linkDefinition, nodes, links, nodesWithIncomingLinks, diagnostics);
            }
        }

        if (diagnostics.Count > 0)
        {
            await CleanupAsync(nodes.Values, links, diagnostics).ConfigureAwait(false);
            return CompositionBuildResult.Failure(diagnostics);
        }

        return CompositionBuildResult.Success(
            new CompositionRuntime(nodes.Values.ToArray(), links, nodesWithIncomingLinks));
    }

    private static void ValidateDescriptorPorts(
        RuntimeNodeKey key,
        CompositionNodeRegistration registration,
        ComposedNode descriptor,
        List<CompositionDiagnostic> diagnostics)
    {
        foreach (var (portName, metadata) in registration.Inputs)
        {
            if (!descriptor.Inputs.TryGetValue(portName, out var input))
            {
                diagnostics.Add(new CompositionDiagnostic
                {
                    Code = CompositionDiagnosticCode.DescriptorPortMismatch,
                    WorkflowName = key.WorkflowName,
                    NodeName = key.NodeName,
                    Message = $"Node '{key}' descriptor is missing input port '{portName}'."
                });
                continue;
            }

            if (input.MessageType != metadata.MessageType)
            {
                diagnostics.Add(new CompositionDiagnostic
                {
                    Code = CompositionDiagnosticCode.DescriptorPortMismatch,
                    WorkflowName = key.WorkflowName,
                    NodeName = key.NodeName,
                    Message =
                        $"Node '{key}' input port '{portName}' is {input.MessageType.Name}, expected {metadata.MessageType.Name}."
                });
            }
        }

        foreach (var (portName, metadata) in registration.Outputs)
        {
            if (!descriptor.Outputs.TryGetValue(portName, out var output))
            {
                diagnostics.Add(new CompositionDiagnostic
                {
                    Code = CompositionDiagnosticCode.DescriptorPortMismatch,
                    WorkflowName = key.WorkflowName,
                    NodeName = key.NodeName,
                    Message = $"Node '{key}' descriptor is missing output port '{portName}'."
                });
                continue;
            }

            if (output.MessageType != metadata.MessageType)
            {
                diagnostics.Add(new CompositionDiagnostic
                {
                    Code = CompositionDiagnosticCode.DescriptorPortMismatch,
                    WorkflowName = key.WorkflowName,
                    NodeName = key.NodeName,
                    Message =
                        $"Node '{key}' output port '{portName}' is {output.MessageType.Name}, expected {metadata.MessageType.Name}."
                });
            }
        }
    }

    private static void LinkNodes(
        string currentWorkflowName,
        LinkDefinition linkDefinition,
        IReadOnlyDictionary<RuntimeNodeKey, CompositionRuntimeNode> nodes,
        List<IDisposable> links,
        HashSet<RuntimeNodeKey> nodesWithIncomingLinks,
        List<CompositionDiagnostic> diagnostics)
    {
        var from = linkDefinition.From.ResolveWorkflow(currentWorkflowName);
        var to = linkDefinition.To.ResolveWorkflow(currentWorkflowName);
        var sourceKey = new RuntimeNodeKey(from.Workflow!, from.Node);
        var targetKey = new RuntimeNodeKey(to.Workflow!, to.Node);

        if (!nodes.TryGetValue(sourceKey, out var sourceNode)
            || !nodes.TryGetValue(targetKey, out var targetNode))
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.MissingNode,
                WorkflowName = currentWorkflowName,
                Link = linkDefinition,
                Message = $"Link '{from} -> {to}' references a node that was not built."
            });
            return;
        }

        if (!sourceNode.Descriptor.Outputs.TryGetValue(from.Port, out var output))
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.MissingOutputPort,
                WorkflowName = currentWorkflowName,
                Link = linkDefinition,
                Message = $"Node '{sourceKey}' does not expose output port '{from.Port}'."
            });
            return;
        }

        if (!targetNode.Descriptor.Inputs.TryGetValue(to.Port, out var input))
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.MissingInputPort,
                WorkflowName = currentWorkflowName,
                Link = linkDefinition,
                Message = $"Node '{targetKey}' does not expose input port '{to.Port}'."
            });
            return;
        }

        if (output.MessageType != input.MessageType)
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.PortTypeMismatch,
                WorkflowName = currentWorkflowName,
                Link = linkDefinition,
                Message =
                    $"Link '{from} -> {to}' connects {output.MessageType.Name} to {input.MessageType.Name}."
            });
            return;
        }

        if (!output.TryLinkTo(input, out var link) || link is null)
        {
            diagnostics.Add(new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.LinkFailed,
                WorkflowName = currentWorkflowName,
                Link = linkDefinition,
                Message = $"Could not link '{from} -> {to}'."
            });
            return;
        }

        links.Add(link);
        nodesWithIncomingLinks.Add(targetKey);
    }

    private static async ValueTask CleanupAsync(
        IEnumerable<CompositionRuntimeNode> nodes,
        IEnumerable<IDisposable> links,
        List<CompositionDiagnostic> diagnostics)
    {
        foreach (var link in links)
        {
            try
            {
                link.Dispose();
            }
            catch (Exception exception)
            {
                diagnostics.Add(new CompositionDiagnostic
                {
                    Code = CompositionDiagnosticCode.CleanupFailed,
                    Exception = exception,
                    Message = $"Failed to dispose a composition link: {exception.Message}"
                });
            }
        }

        foreach (var node in nodes.Reverse())
        {
            try
            {
                await node.Descriptor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new CompositionDiagnostic
                {
                    Code = CompositionDiagnosticCode.CleanupFailed,
                    WorkflowName = node.WorkflowName,
                    NodeName = node.NodeName,
                    Exception = exception,
                    Message = $"Failed to dispose node '{node.WorkflowName}.{node.NodeName}': {exception.Message}"
                });
            }
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
