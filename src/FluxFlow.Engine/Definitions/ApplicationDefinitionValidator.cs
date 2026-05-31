using FluxFlow.Engine.Scenarios;

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

        foreach (var dashboard in definition.Dashboards)
        {
            ValidateDashboard(dashboard.Key, dashboard.Value, errors);
        }

        foreach (var test in definition.Tests)
        {
            ValidateScenario(test.Key, test.Value, definition, errors);
        }

        return new ApplicationDefinitionValidationResult(errors);
    }

    private static void ValidateDashboard(
        string dashboardName,
        DashboardDefinition dashboard,
        List<ApplicationDefinitionValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(dashboardName))
        {
            errors.Add(new(
                ApplicationDefinitionValidationErrorCode.EmptyDashboardName,
                "Dashboard name cannot be empty."));
        }

        if (dashboard.Layout.Columns.Count == 0)
        {
            errors.Add(new(
                ApplicationDefinitionValidationErrorCode.InvalidDashboardLayout,
                $"Dashboard '{dashboardName}' must define at least one column track."));
        }

        if (dashboard.Layout.Rows.Count == 0)
        {
            errors.Add(new(
                ApplicationDefinitionValidationErrorCode.InvalidDashboardLayout,
                $"Dashboard '{dashboardName}' must define at least one row track."));
        }

        ValidateTracks(dashboardName, "column", dashboard.Layout.Columns, errors);
        ValidateTracks(dashboardName, "row", dashboard.Layout.Rows, errors);
        ValidateTrackPadding(dashboardName, "column", dashboard.Layout.ColumnPadding, errors);
        ValidateTrackPadding(dashboardName, "row", dashboard.Layout.RowPadding, errors);

        foreach (var widget in dashboard.Widgets)
        {
            if (string.IsNullOrWhiteSpace(widget.Key))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.EmptyDashboardWidgetName,
                    $"Dashboard '{dashboardName}' has an empty widget name."));
            }

            if (string.IsNullOrWhiteSpace(widget.Value.Type))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.EmptyDashboardWidgetType,
                    $"Dashboard '{dashboardName}' widget '{widget.Key}' has an empty type."));
            }
        }

        foreach (var cell in dashboard.Layout.Cells)
        {
            if (string.IsNullOrWhiteSpace(cell.Key))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.EmptyDashboardCellName,
                    $"Dashboard '{dashboardName}' has an empty cell name."));
            }

            if (cell.Value.Row < 0 ||
                cell.Value.Column < 0 ||
                cell.Value.RowSpan <= 0 ||
                cell.Value.ColumnSpan <= 0)
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.InvalidDashboardCell,
                    $"Dashboard '{dashboardName}' cell '{cell.Key}' has an invalid grid position or span."));
            }

            if (cell.Value.Row >= 0 &&
                cell.Value.RowSpan > 0 &&
                dashboard.Layout.Rows.Count > 0 &&
                cell.Value.Row + cell.Value.RowSpan > dashboard.Layout.Rows.Count)
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.InvalidDashboardCell,
                    $"Dashboard '{dashboardName}' cell '{cell.Key}' extends beyond the defined row tracks."));
            }

            if (cell.Value.Column >= 0 &&
                cell.Value.ColumnSpan > 0 &&
                dashboard.Layout.Columns.Count > 0 &&
                cell.Value.Column + cell.Value.ColumnSpan > dashboard.Layout.Columns.Count)
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.InvalidDashboardCell,
                    $"Dashboard '{dashboardName}' cell '{cell.Key}' extends beyond the defined column tracks."));
            }

            if (!string.IsNullOrWhiteSpace(cell.Value.Widget) &&
                !dashboard.Widgets.ContainsKey(cell.Value.Widget))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.MissingDashboardWidget,
                    $"Dashboard '{dashboardName}' cell '{cell.Key}' references missing widget '{cell.Value.Widget}'."));
            }
        }
    }

    private static void ValidateTracks(
        string dashboardName,
        string axis,
        IReadOnlyList<DashboardGridTrackDefinition> tracks,
        List<ApplicationDefinitionValidationError> errors)
    {
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            if (track.Value <= 0 ||
                double.IsNaN(track.Value) ||
                double.IsInfinity(track.Value))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.InvalidDashboardLayout,
                    $"Dashboard '{dashboardName}' {axis} track {i} must be a positive finite size."));
            }

            if (track.Unit == DashboardGridTrackUnit.Percent && track.Value > 100)
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.InvalidDashboardLayout,
                    $"Dashboard '{dashboardName}' {axis} track {i} percent size cannot exceed 100."));
            }
        }
    }

    private static void ValidateTrackPadding(
        string dashboardName,
        string axis,
        IReadOnlyList<double> padding,
        List<ApplicationDefinitionValidationError> errors)
    {
        for (var i = 0; i < padding.Count; i++)
        {
            if (padding[i] < 0 || double.IsNaN(padding[i]) || double.IsInfinity(padding[i]))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.InvalidDashboardLayout,
                    $"Dashboard '{dashboardName}' {axis} track {i} padding must be a non-negative finite size."));
            }
        }
    }

    private static void ValidateScenario(
        string scenarioName,
        ScenarioDefinition scenario,
        ApplicationDefinition definition,
        List<ApplicationDefinitionValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(scenarioName))
        {
            errors.Add(new(
                ApplicationDefinitionValidationErrorCode.EmptyScenarioName,
                "Test scenario name cannot be empty."));
        }

        foreach (var step in scenario.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Key))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.EmptyScenarioStepName,
                    $"Test scenario '{scenarioName}' has an empty step name."));
            }

            if (string.IsNullOrWhiteSpace(step.Value.Type))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.EmptyScenarioStepType,
                    $"Test scenario '{scenarioName}' step '{step.Key}' has an empty type."));
            }
            else if (!ScenarioStepTypes.All.Contains(step.Value.Type))
            {
                errors.Add(new(
                    ApplicationDefinitionValidationErrorCode.UnknownScenarioStepType,
                    $"Test scenario '{scenarioName}' step '{step.Key}' has unknown type '{step.Value.Type}'."));
            }
            else
            {
                ScenarioStepDefinitionValidator.Validate(
                    scenarioName,
                    step.Key,
                    step.Value,
                    definition,
                    errors);
            }
        }
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
