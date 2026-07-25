using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Ports;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimePortSurfaceFactory(
    CompositionNodeRegistry registry,
    ApplicationRuntimeAssemblerOptions options,
    ILogger? logger)
{
    internal IReadOnlyList<ApplicationRuntimePortSurfaceEntry> Describe(
        ApplicationDefinition definition)
    {
        var entries = new List<ApplicationRuntimePortSurfaceEntry>();
        foreach (var (workflowName, workflow) in definition.Workflows)
        {
            foreach (var (componentName, component) in workflow.Components)
            {
                if (!registry.TryGetRegistration(component.Type, out var registration))
                {
                    throw new ApplicationRuntimeAssemblerException(
                        $"Component '{workflowName}.{componentName}' uses unregistered type '{component.Type}'.");
                }

                foreach (var metadata in registration.Inputs.Values)
                {
                    AddEntry(
                        entries,
                        workflowName,
                        componentName,
                        metadata,
                        ApplicationPortDirection.Input);
                }

                foreach (var metadata in registration.Outputs.Values)
                {
                    AddEntry(
                        entries,
                        workflowName,
                        componentName,
                        metadata,
                        ApplicationPortDirection.Output);
                }
            }
        }

        return entries
            .OrderBy(static entry => entry.Address.Value, StringComparer.Ordinal)
            .ToArray();
    }

    internal ApplicationPortRuntime Create(
        IReadOnlyList<ApplicationRuntimePortSurfaceEntry> surface)
    {
        var builder = new ApplicationPortRuntimeBuilder();
        if (logger is not null)
            builder.UseLogger(logger);

        foreach (var entry in surface)
        {
            entry.Metadata.Accept(new RuntimePortBuilderVisitor(
                builder,
                entry.Address,
                entry.Direction,
                options));
        }

        return builder.Build();
    }

    internal static bool IsSame(
        IReadOnlyList<ApplicationRuntimePortSurfaceEntry> current,
        IReadOnlyList<ApplicationRuntimePortSurfaceEntry> next)
        => current.Count == next.Count &&
           current.Zip(next).All(static pair => pair.First.IsSame(pair.Second));

    private static void AddEntry(
        ICollection<ApplicationRuntimePortSurfaceEntry> entries,
        string workflowName,
        string componentName,
        CompositionPortMetadata metadata,
        ApplicationPortDirection direction)
    {
        if (!metadata.SupportsTypeVisit)
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Component '{workflowName}.{componentName}' port '{metadata.Name}' does not carry " +
                "reflection-free typed metadata.");
        }

        entries.Add(new ApplicationRuntimePortSurfaceEntry(
            ApplicationAddress.WorkflowPort(workflowName, componentName, metadata.Name),
            direction,
            metadata));
    }

    private sealed class RuntimePortBuilderVisitor(
        ApplicationPortRuntimeBuilder builder,
        ApplicationAddress address,
        ApplicationPortDirection direction,
        ApplicationRuntimeAssemblerOptions options) : ICompositionPortTypeVisitor
    {
        public void Visit<TMessage>(CompositionPortMetadata metadata)
        {
            if (direction == ApplicationPortDirection.Input)
                builder.AddInput<TMessage>(address, options.InputCapacity);
            else
                builder.AddOutput<TMessage>(address, options.OutputCapacity);
        }

        public void VisitSignal(CompositionPortMetadata metadata)
        {
            if (direction != ApplicationPortDirection.Input)
                throw new ApplicationRuntimeAssemblerException($"Signal output '{address}' is unsupported.");
            builder.AddSignalInput(address, options.InputCapacity);
        }
    }
}

internal sealed record ApplicationRuntimePortSurfaceEntry(
    ApplicationAddress Address,
    ApplicationPortDirection Direction,
    CompositionPortMetadata Metadata)
{
    internal bool IsSame(ApplicationRuntimePortSurfaceEntry other)
        => Address == other.Address &&
           Direction == other.Direction &&
           Metadata.Kind == other.Metadata.Kind &&
           Metadata.MessageType == other.Metadata.MessageType;
}
