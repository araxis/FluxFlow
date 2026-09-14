using FluxFlow.Composition.Definition;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Json.Model;
using FluxFlow.Engine.Ports;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimePortSurfaceFactory(
    ApplicationRuntimeAssemblerOptions options,
    ILogger? logger,
    IApplicationOutputCaptureResolver? outputCaptures)
{
    internal IReadOnlyList<ApplicationRuntimePortSurfaceEntry> Describe(
        ApplicationDefinition definition,
        IReadOnlyDictionary<string, ComponentInstance> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var entries = new List<ApplicationRuntimePortSurfaceEntry>();
        foreach (var (workflowName, workflow) in definition.Workflows)
        {
            foreach (var (componentName, component) in workflow.Components)
            {
                if (!catalog.TryGetValue($"{workflowName}.{componentName}", out var descriptor))
                {
                    throw new ApplicationRuntimeAssemblerException(
                        $"Component '{workflowName}.{componentName}' uses unregistered type '{component.Type}'.");
                }

                foreach (var metadata in descriptor.InputMetadata.Values)
                {
                    AddEntry(
                        entries,
                        workflowName,
                        componentName,
                        metadata,
                        ApplicationPortDirection.Input);
                }

                foreach (var metadata in descriptor.OutputMetadata.Values)
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
        var builder = new ApplicationPortRuntimeBuilder(outputCaptures);
        if (logger is not null)
            builder.UseLogger(logger);

        foreach (var entry in surface)
        {
            if (entry.Metadata.Kind == ComponentPortKind.Signal)
            {
                if (entry.Direction != ApplicationPortDirection.Input)
                    throw new ApplicationRuntimeAssemblerException($"Signal output '{entry.Address}' is unsupported.");
                builder.AddSignalInput(entry.Address, options.InputCapacity);
                continue;
            }

            if (entry.Metadata.MessageType != typeof(global::FluxFlow.Data.FlowValue))
                throw new ApplicationRuntimeAssemblerException($"Port '{entry.Address}' must use canonical workflow messages.");
            if (entry.Direction == ApplicationPortDirection.Input)
                builder.AddInput(entry.Address, options.InputCapacity);
            else
                builder.AddOutput(entry.Address, options.OutputCapacity);
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
        ComponentPortMetadata metadata,
        ApplicationPortDirection direction)
    {
        if (metadata.Kind == ComponentPortKind.Message && metadata.MessageType != typeof(global::FluxFlow.Data.FlowValue))
        {
            throw new ApplicationRuntimeAssemblerException(
                $"Component '{workflowName}.{componentName}' port '{metadata.Name}' must use canonical workflow messages.");
        }

        entries.Add(new ApplicationRuntimePortSurfaceEntry(
            ApplicationAddress.WorkflowPort(workflowName, componentName, metadata.Name),
            direction,
            metadata));
    }
}

internal sealed record ApplicationRuntimePortSurfaceEntry(
    ApplicationAddress Address,
    ApplicationPortDirection Direction,
    ComponentPortMetadata Metadata)
{
    internal bool IsSame(ApplicationRuntimePortSurfaceEntry other)
        => Address == other.Address &&
           Direction == other.Direction &&
           Metadata.Kind == other.Metadata.Kind &&
           Metadata.MessageType == other.Metadata.MessageType;
}
