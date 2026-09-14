using FluxFlow.Composition;
using FluxFlow.Core;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeDrainPlan(
    IReadOnlyList<ApplicationRuntimeDrainStage> stages)
{
    internal static ApplicationRuntimeDrainPlan? TryCreate(
        IReadOnlyList<CompiledApplicationLink> links,
        IReadOnlyDictionary<ApplicationRuntimeComponentKey, ComponentInstance> components)
    {
        var keys = components.Keys
            .OrderBy(static key => key.WorkflowName, StringComparer.Ordinal)
            .ThenBy(static key => key.ComponentName, StringComparer.Ordinal)
            .SelectMany(key => components[key].Node is IFlowSource
                ? new[] { new DrainKey(key, false), new DrainKey(key, true) }
                : [new DrainKey(key, false)])
            .ToArray();
        var layers = FlowDependencyOrder.TryCreate(keys, Dependencies());
        return layers is not null
            ? new ApplicationRuntimeDrainPlan(layers.Select(layer => CreateStage(layer, components)).ToArray())
            : null;

        IEnumerable<(DrainKey Source, DrainKey Target)> Dependencies()
        {
            // Production precedes control completion; event output follows control completion.
            foreach (var key in keys.Where(key => key.Controls))
                yield return (key with { Controls = false }, key);
            foreach (var link in links)
            {
                var source = ComponentKey(link.Source);
                var target = ComponentKey(link.Target);
                if (!components.TryGetValue(source, out var producer) ||
                    !components.TryGetValue(target, out var consumer)) continue;
                var from = new DrainKey(source, producer.Node is IFlowSource &&
                    producer.Outputs[link.Source.Segments[2]].IsEvent);
                var to = new DrainKey(target, consumer.Node is IFlowSource &&
                    IsControlInput(consumer, link.Target.Segments[2]));
                if (from != to) yield return (from, to);
            }
        }
    }

    internal async ValueTask DrainAsync(
        Task completion,
        ApplicationPortRevisionLease ports,
        CancellationToken cancellationToken)
    {
        foreach (var stage in stages)
        {
            await ports.DrainInputsAsync(stage.Inputs, cancellationToken).ConfigureAwait(false);
            foreach (var step in stage.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (step.StopProduction) ((IFlowSource)step.Component.Node).StopProduction();
                else step.Component.Node.Complete();
            }

            await Task.WhenAll(stage.Steps.Select(static step => step.StopProduction
                    ? ((IFlowSource)step.Component.Node).ProductionCompletion : step.Component.Completion))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            await ports.DrainOutputsAsync(stage.Outputs, cancellationToken).ConfigureAwait(false);
        }

        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsControlInput(ComponentInstance component, string name)
        => component.Node is IFlowComponent node && component.Inputs.TryGetValue(name, out var port)
            && node.Ports.IsControlInput(port.Target);

    private static ApplicationRuntimeComponentKey ComponentKey(ApplicationAddress port)
        => new(port.Segments[0], port.Segments[1]);

    private static ApplicationRuntimeDrainStage CreateStage(
        IReadOnlyList<DrainKey> keys,
        IReadOnlyDictionary<ApplicationRuntimeComponentKey, ComponentInstance> components)
    {
        var steps = keys.Select(key => new ApplicationRuntimeDrainStep(components[key.Component],
            !key.Controls && components[key.Component].Node is IFlowSource)).ToArray();
        var inputs = keys.SelectMany(key => components[key.Component].Inputs.Values
            .Where(port => IsControlInput(components[key.Component], port.Name) == key.Controls)
            .Select(port => Address(key, port.Name))).ToHashSet();
        var outputs = keys.SelectMany(key => components[key.Component].Outputs.Values
            .Where(port => components[key.Component].Node is not IFlowSource || port.IsEvent == key.Controls)
            .Select(port => Address(key, port.Name))).ToHashSet();
        return new ApplicationRuntimeDrainStage(steps, inputs, outputs);
    }

    private static ApplicationAddress Address(DrainKey key, string port)
        => ApplicationAddress.WorkflowPort(key.Component.WorkflowName, key.Component.ComponentName, port);

    private readonly record struct DrainKey(ApplicationRuntimeComponentKey Component, bool Controls);
}

internal sealed record ApplicationRuntimeDrainStep(ComponentInstance Component, bool StopProduction);

internal sealed record ApplicationRuntimeDrainStage(
    IReadOnlyList<ApplicationRuntimeDrainStep> Steps,
    IReadOnlySet<ApplicationAddress> Inputs,
    IReadOnlySet<ApplicationAddress> Outputs);
