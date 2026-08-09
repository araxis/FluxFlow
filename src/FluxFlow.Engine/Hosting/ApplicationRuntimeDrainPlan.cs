using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Ports;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeDrainPlan(
    IReadOnlyList<ApplicationRuntimeDrainStage> stages)
{
    internal static ApplicationRuntimeDrainPlan? TryCreate(
        ApplicationDefinition definition,
        IReadOnlyDictionary<ApplicationRuntimeComponentKey, ApplicationRuntimeBuiltComponent> components)
    {
        var upstreams = components.Keys.ToDictionary(
            static key => key,
            static _ => new HashSet<ApplicationRuntimeComponentKey>());
        var downstreams = components.Keys.ToDictionary(
            static key => key,
            static _ => new HashSet<ApplicationRuntimeComponentKey>());

        foreach (var link in definition.Links)
        {
            var source = ComponentKey(link.Source);
            var target = ComponentKey(link.Target);
            if (source == target || !components.ContainsKey(source) || !components.ContainsKey(target))
                continue;

            upstreams[target].Add(source);
            downstreams[source].Add(target);
        }

        var remaining = upstreams.ToDictionary(
            static item => item.Key,
            static item => item.Value.Count);
        var ready = remaining
            .Where(static item => item.Value == 0)
            .Select(static item => item.Key)
            .OrderBy(static key => key.WorkflowName, StringComparer.Ordinal)
            .ThenBy(static key => key.ComponentName, StringComparer.Ordinal)
            .ToArray();
        var planned = new List<ApplicationRuntimeDrainStage>();
        var visited = 0;

        while (ready.Length > 0)
        {
            planned.Add(CreateStage(ready, components));
            visited += ready.Length;
            var next = new HashSet<ApplicationRuntimeComponentKey>();
            foreach (var source in ready)
            {
                foreach (var target in downstreams[source])
                {
                    if (--remaining[target] == 0)
                        next.Add(target);
                }
            }

            ready = next
                .OrderBy(static key => key.WorkflowName, StringComparer.Ordinal)
                .ThenBy(static key => key.ComponentName, StringComparer.Ordinal)
                .ToArray();
        }

        return visited == components.Count
            ? new ApplicationRuntimeDrainPlan(planned)
            : null;
    }

    internal async ValueTask DrainAsync(
        ApplicationRuntime runtime,
        ApplicationPortRevisionLease ports,
        CancellationToken cancellationToken)
    {
        foreach (var stage in stages)
        {
            await ports.DrainInputsAsync(stage.Inputs, cancellationToken).ConfigureAwait(false);
            foreach (var component in stage.Components)
            {
                cancellationToken.ThrowIfCancellationRequested();
                component.Node.Complete();
            }

            await Task.WhenAll(stage.Components.Select(static component => component.Completion))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            await ports.DrainOutputsAsync(stage.Outputs, cancellationToken).ConfigureAwait(false);
        }

        await runtime.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ApplicationRuntimeComponentKey ComponentKey(ApplicationAddress port)
        => new(port.Segments[0], port.Segments[1]);

    private static ApplicationRuntimeDrainStage CreateStage(
        IReadOnlyList<ApplicationRuntimeComponentKey> keys,
        IReadOnlyDictionary<ApplicationRuntimeComponentKey, ApplicationRuntimeBuiltComponent> components)
    {
        var instances = keys.Select(key => components[key].Instance).ToArray();
        var inputs = keys
            .SelectMany(key => components[key].Instance.Inputs.Keys.Select(port =>
                ApplicationAddress.WorkflowPort(key.WorkflowName, key.ComponentName, port)))
            .ToHashSet();
        var outputs = keys
            .SelectMany(key => components[key].Instance.Outputs.Keys.Select(port =>
                ApplicationAddress.WorkflowPort(key.WorkflowName, key.ComponentName, port)))
            .ToHashSet();
        return new ApplicationRuntimeDrainStage(instances, inputs, outputs);
    }
}

internal sealed record ApplicationRuntimeDrainStage(
    IReadOnlyList<ComponentInstance> Components,
    IReadOnlySet<ApplicationAddress> Inputs,
    IReadOnlySet<ApplicationAddress> Outputs);
