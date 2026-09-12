using FluxFlow.Composition;
using System.Text.Json;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using ApplicationWorkflowDefinition = FluxFlow.Json.Model.WorkflowDefinition;

namespace FluxFlow.Engine.Internal.Revisions;

internal sealed class ApplicationRevisionPlanner
{
    public ApplicationRevisionPlan Plan(
        ApplicationSetup current,
        ApplicationSetup next)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);

        var currentResources = FlattenResources(current.Definition);
        var nextResources = FlattenResources(next.Definition);
        var resourceChanges = CompareResources(
            currentResources,
            nextResources,
            current,
            next);
        var workflowChanges = CompareWorkflows(current, next);
        var diagnostics = new List<ApplicationRevisionDiagnostic>();
        var resourceDependencies = ReadResourceDependencies(nextResources, diagnostics);
        var workflowDependencies = ReadWorkflowDependencies(next.Definition.Workflows, nextResources, diagnostics);

        AddCycleDiagnostics(resourceDependencies, diagnostics);

        var changedResources = resourceChanges
            .Select(static change => change.Address)
            .ToHashSet();
        var affectedResources = ExpandResourceDependents(
            changedResources,
            resourceDependencies);
        var affectedWorkflows = workflowChanges
            .Select(static change => change.Workflow)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (workflow, dependencies) in workflowDependencies)
        {
            if (dependencies.Any(affectedResources.Contains))
                affectedWorkflows.Add(workflow);
        }

        return new ApplicationRevisionPlan(
            current,
            next,
            resourceChanges,
            workflowChanges,
            affectedResources
                .Where(nextResources.ContainsKey)
                .OrderBy(static address => address.Value, StringComparer.Ordinal)
                .ToArray(),
            affectedWorkflows
                .Where(next.Definition.Workflows.ContainsKey)
                .OrderBy(static workflow => workflow, StringComparer.Ordinal)
                .ToArray(),
            diagnostics
                .OrderBy(static diagnostic => diagnostic.Location, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Code)
                .ToArray());
    }

    private static IReadOnlyDictionary<ApplicationAddress, ResourceInstanceDefinition> FlattenResources(
        ApplicationDefinition definition)
    {
        var resources = new Dictionary<ApplicationAddress, ResourceInstanceDefinition>();
        foreach (var (name, resource) in definition.Resources)
            AddResource(resources, [name], resource);
        return resources;
    }

    private static void AddResource(
        IDictionary<ApplicationAddress, ResourceInstanceDefinition> resources,
        IReadOnlyList<string> path,
        ResourceDefinition resource)
    {
        if (resource is ResourceInstanceDefinition instance)
        {
            resources.Add(ApplicationAddress.Resource(path.ToArray()), instance);
            return;
        }

        var group = (ResourceGroupDefinition)resource;
        foreach (var (name, child) in group.Resources)
            AddResource(resources, [.. path, name], child);
    }

    private static IReadOnlyList<ApplicationResourceRevisionChange> CompareResources(
        IReadOnlyDictionary<ApplicationAddress, ResourceInstanceDefinition> current,
        IReadOnlyDictionary<ApplicationAddress, ResourceInstanceDefinition> next,
        ApplicationSetup currentDefinition,
        ApplicationSetup nextDefinition)
    {
        var addresses = current.Keys
            .Concat(next.Keys)
            .Distinct()
            .OrderBy(static address => address.Value, StringComparer.Ordinal);
        var changes = new List<ApplicationResourceRevisionChange>();

        foreach (var address in addresses)
        {
            var hasCurrent = current.TryGetValue(address, out var currentResource);
            var hasNext = next.TryGetValue(address, out var nextResource);
            var kind = hasCurrent && hasNext
                ? ResourceEquals(currentResource!, nextResource!) &&
                  ResourceContractEquals(
                      currentResource!.Type,
                      currentDefinition,
                      nextDefinition)
                    ? (ApplicationRevisionChangeKind?)null
                    : ApplicationRevisionChangeKind.Updated
                : hasNext
                    ? ApplicationRevisionChangeKind.Added
                    : ApplicationRevisionChangeKind.Removed;

            if (kind is not null)
            {
                changes.Add(new ApplicationResourceRevisionChange
                {
                    Address = address,
                    Kind = kind.Value
                });
            }
        }

        return changes;
    }

    private static IReadOnlyList<ApplicationWorkflowRevisionChange> CompareWorkflows(
        ApplicationSetup current,
        ApplicationSetup next)
    {
        var names = current.Definition.Workflows.Keys
            .Concat(next.Definition.Workflows.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal);
        var changes = new List<ApplicationWorkflowRevisionChange>();

        foreach (var name in names)
        {
            var hasCurrent = current.Definition.Workflows.TryGetValue(name, out var currentWorkflow);
            var hasNext = next.Definition.Workflows.TryGetValue(name, out var nextWorkflow);
            var kind = hasCurrent && hasNext
                ? WorkflowEquals(currentWorkflow!, nextWorkflow!) &&
                  string.Equals(currentWorkflow!.ExpressionEngine ?? current.Definition.ExpressionEngine,
                      nextWorkflow!.ExpressionEngine ?? next.Definition.ExpressionEngine, StringComparison.Ordinal) &&
                  WorkflowLinksEqual(name, current.Links, next.Links) &&
                  WorkflowComponentContractsEqual(currentWorkflow!, current, next)
                    ? (ApplicationRevisionChangeKind?)null
                    : ApplicationRevisionChangeKind.Updated
                : hasNext
                    ? ApplicationRevisionChangeKind.Added
                    : ApplicationRevisionChangeKind.Removed;

            if (kind is not null)
            {
                changes.Add(new ApplicationWorkflowRevisionChange
                {
                    Workflow = name,
                    Kind = kind.Value
                });
            }
        }

        return changes;
    }

    private static bool ResourceContractEquals(
        string resourceType,
        ApplicationSetup current,
        ApplicationSetup next)
    {
        var currentContract = current.ResourceContracts
            .SingleOrDefault(contract => string.Equals(
                contract.Type,
                resourceType,
                StringComparison.Ordinal));
        var nextContract = next.ResourceContracts
            .SingleOrDefault(contract => string.Equals(
                contract.Type,
                resourceType,
                StringComparison.Ordinal));
        return ReferenceEquals(currentContract, nextContract);
    }

    private static bool WorkflowLinksEqual(
        string workflow,
        IReadOnlyList<ApplicationLinkDefinition> current,
        IReadOnlyList<ApplicationLinkDefinition> next)
        => current.Where(link => BelongsToWorkflow(link, workflow))
            .SequenceEqual(next.Where(link => BelongsToWorkflow(link, workflow)));

    private static bool WorkflowComponentContractsEqual(
        ApplicationWorkflowDefinition workflow,
        ApplicationSetup current,
        ApplicationSetup next)
    {
        var currentDescriptors = current.ComponentDescriptors.ToDictionary(
            static descriptor => descriptor.Type,
            StringComparer.Ordinal);
        var nextDescriptors = next.ComponentDescriptors.ToDictionary(
            static descriptor => descriptor.Type,
            StringComparer.Ordinal);

        foreach (var type in workflow.Components.Values
                     .Select(static component => component.Type)
                     .Distinct(StringComparer.Ordinal))
        {
            var hasCurrent = currentDescriptors.TryGetValue(type, out var currentDescriptor);
            var hasNext = nextDescriptors.TryGetValue(type, out var nextDescriptor);
            if (hasCurrent != hasNext ||
                hasCurrent && !ReferenceEquals(currentDescriptor, nextDescriptor))
            {
                return false;
            }
        }

        return true;
    }

    private static bool BelongsToWorkflow(
        ApplicationLinkDefinition link,
        string workflow)
        => IsWorkflow(link.Source, workflow) || IsWorkflow(link.Target, workflow);

    private static bool IsWorkflow(ApplicationAddress address, string workflow)
        => address.Kind == ApplicationAddressKind.WorkflowPort &&
           string.Equals(address.Segments[0], workflow, StringComparison.Ordinal);

    private static IReadOnlyDictionary<ApplicationAddress, HashSet<ApplicationAddress>> ReadResourceDependencies(
        IReadOnlyDictionary<ApplicationAddress, ResourceInstanceDefinition> resources,
        ICollection<ApplicationRevisionDiagnostic> diagnostics)
    {
        var dependencies = new Dictionary<ApplicationAddress, HashSet<ApplicationAddress>>();
        foreach (var (address, resource) in resources)
        {
            var references = ReadReferences(resource.Properties.Values);
            dependencies.Add(address, references);
            AddMissingReferenceDiagnostics(
                address.Value,
                references,
                resources,
                diagnostics);
        }

        return dependencies;
    }

    private static IReadOnlyDictionary<string, HashSet<ApplicationAddress>> ReadWorkflowDependencies(
        IReadOnlyDictionary<string, ApplicationWorkflowDefinition> workflows,
        IReadOnlyDictionary<ApplicationAddress, ResourceInstanceDefinition> resources,
        ICollection<ApplicationRevisionDiagnostic> diagnostics)
    {
        var dependencies = new Dictionary<string, HashSet<ApplicationAddress>>(StringComparer.Ordinal);
        foreach (var (workflowName, workflow) in workflows)
        {
            var references = ReadReferences(
                workflow.Components.Values.SelectMany(static component => component.Properties.Values),
                skipLinkData: true);
            dependencies.Add(workflowName, references);
            AddMissingReferenceDiagnostics(
                workflowName,
                references,
                resources,
                diagnostics);
        }

        return dependencies;
    }

    private static HashSet<ApplicationAddress> ReadReferences(IEnumerable<JsonElement> values, bool skipLinkData = false)
    {
        var references = new HashSet<ApplicationAddress>();
        foreach (var value in values)
            AddReferences(value, references, skipLinkData);
        return references;
    }

    private static void AddReferences(
        JsonElement value,
        ISet<ApplicationAddress> references,
        bool skipLinkData)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                if (ApplicationAddress.TryParse(value.GetString(), out var address) &&
                    address!.Kind == ApplicationAddressKind.Resource)
                {
                    references.Add(address);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    AddReferences(item, references, skipLinkData);
                break;
            case JsonValueKind.Object:
                // Conditional-link expressions and parameters are literal data, not resource references.
                if (skipLinkData && value.TryGetProperty("Port", out var port) &&
                    port.ValueKind == JsonValueKind.String && value.TryGetProperty("Condition", out var condition) &&
                    condition.ValueKind == JsonValueKind.String)
                {
                    AddReferences(port, references, skipLinkData);
                    break;
                }
                foreach (var property in value.EnumerateObject())
                    AddReferences(property.Value, references, skipLinkData);
                break;
        }
    }

    private static void AddMissingReferenceDiagnostics(
        string location,
        IEnumerable<ApplicationAddress> references,
        IReadOnlyDictionary<ApplicationAddress, ResourceInstanceDefinition> resources,
        ICollection<ApplicationRevisionDiagnostic> diagnostics)
    {
        foreach (var reference in references.Where(reference => !resources.ContainsKey(reference)))
        {
            diagnostics.Add(new ApplicationRevisionDiagnostic
            {
                Code = ApplicationRevisionDiagnosticCode.MissingResourceReference,
                Location = location,
                Resource = reference,
                Message = $"'{location}' references missing resource '{reference}'."
            });
        }
    }

    private static HashSet<ApplicationAddress> ExpandResourceDependents(
        HashSet<ApplicationAddress> changed,
        IReadOnlyDictionary<ApplicationAddress, HashSet<ApplicationAddress>> dependencies)
    {
        var dependents = new Dictionary<ApplicationAddress, List<ApplicationAddress>>();
        foreach (var (resource, references) in dependencies)
        {
            foreach (var reference in references)
            {
                if (!dependents.TryGetValue(reference, out var values))
                {
                    values = [];
                    dependents.Add(reference, values);
                }

                values.Add(resource);
            }
        }

        var affected = new HashSet<ApplicationAddress>(changed);
        var pending = new Queue<ApplicationAddress>(changed);
        while (pending.TryDequeue(out var resource))
        {
            if (!dependents.TryGetValue(resource, out var values))
                continue;
            foreach (var dependent in values)
            {
                if (affected.Add(dependent))
                    pending.Enqueue(dependent);
            }
        }

        return affected;
    }

    private static void AddCycleDiagnostics(
        IReadOnlyDictionary<ApplicationAddress, HashSet<ApplicationAddress>> dependencies,
        ICollection<ApplicationRevisionDiagnostic> diagnostics)
    {
        var state = new Dictionary<ApplicationAddress, VisitState>();
        var path = new List<ApplicationAddress>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resource in dependencies.Keys.OrderBy(static value => value.Value, StringComparer.Ordinal))
            Visit(resource);

        void Visit(ApplicationAddress resource)
        {
            if (state.TryGetValue(resource, out var existing))
            {
                if (existing != VisitState.Visiting)
                    return;

                var start = path.FindIndex(value => value.Equals(resource));
                var cycle = path.Skip(start).Append(resource).ToArray();
                var text = string.Join(" -> ", cycle.Select(static value => value.Value));
                if (reported.Add(text))
                {
                    diagnostics.Add(new ApplicationRevisionDiagnostic
                    {
                        Code = ApplicationRevisionDiagnosticCode.ResourceDependencyCycle,
                        Location = resource.Value,
                        Resource = resource,
                        Message = $"Resource dependency cycle detected: {text}."
                    });
                }

                return;
            }

            state[resource] = VisitState.Visiting;
            path.Add(resource);
            if (dependencies.TryGetValue(resource, out var references))
            {
                foreach (var reference in references
                             .Where(dependencies.ContainsKey)
                             .OrderBy(static value => value.Value, StringComparer.Ordinal))
                {
                    Visit(reference);
                }
            }

            path.RemoveAt(path.Count - 1);
            state[resource] = VisitState.Visited;
        }
    }

    private static bool ResourceEquals(
        ResourceInstanceDefinition left,
        ResourceInstanceDefinition right)
        => string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
           PropertiesEqual(left.Properties, right.Properties);

    private static bool WorkflowEquals(
        ApplicationWorkflowDefinition left,
        ApplicationWorkflowDefinition right)
    {
        if (left.Components.Count != right.Components.Count)
            return false;
        foreach (var (name, component) in left.Components)
        {
            if (!right.Components.TryGetValue(name, out var other) ||
                !string.Equals(component.Type, other.Type, StringComparison.Ordinal) ||
                !PropertiesEqual(component.Properties, other.Properties))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PropertiesEqual(
        IReadOnlyDictionary<string, JsonElement> left,
        IReadOnlyDictionary<string, JsonElement> right)
    {
        if (left.Count != right.Count)
            return false;
        foreach (var (name, value) in left)
        {
            if (!right.TryGetValue(name, out var other) || !JsonEqual(value, other))
                return false;
        }

        return true;
    }

    private static bool JsonEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        return left.ValueKind switch
        {
            JsonValueKind.Object => ObjectEqual(left, right),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength() &&
                                   left.EnumerateArray().Zip(right.EnumerateArray())
                                       .All(static pair => JsonEqual(pair.First, pair.Second)),
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null => true,
            _ => false
        };
    }

    private static bool ObjectEqual(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value, StringComparer.Ordinal);
        var rightProperties = right.EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value, StringComparer.Ordinal);
        return PropertiesEqual(leftProperties, rightProperties);
    }

    private enum VisitState
    {
        Visiting,
        Visited
    }
}
