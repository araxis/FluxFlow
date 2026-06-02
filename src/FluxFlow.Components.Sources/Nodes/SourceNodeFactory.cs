using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Sources.Nodes;

internal static class SourceNodeFactory
{
    private static readonly MethodInfo CreateGeneratedMethod = GetMethod(nameof(CreateGeneratedTyped));

    public static RuntimeNode CreateGenerated(
        RuntimeNodeFactoryContext context,
        SourcesComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = SourceOptionsReader.ReadGeneratedOptions(context.Definition);
        var outputType = componentOptions.ResolveType(options.EffectiveOutputType);

        try
        {
            var method = CreateGeneratedMethod.MakeGenericMethod(outputType);
            return (RuntimeNode)method.Invoke(null, [context, options, componentOptions])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public static RuntimeNode CreateSequence(
        RuntimeNodeFactoryContext context,
        SourcesComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = SourceOptionsReader.ReadSequenceOptions(context.Definition);
        var node = new SequenceSourceNode(options, componentOptions.Clock);
        return context.CreateNode(node)
            .Output(SourcesComponentPorts.Output, node.Output)
            .Output(SourcesComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static RuntimeNode CreateGeneratedTyped<TOutput>(
        RuntimeNodeFactoryContext context,
        GeneratedSourceOptions options,
        SourcesComponentOptions componentOptions)
    {
        var items = componentOptions.DeserializeItems<TOutput>(options);
        var node = new GeneratedSourceNode<TOutput>(options, items, componentOptions.Clock);
        return context.CreateNode(node)
            .Output(SourcesComponentPorts.Output, node.Output)
            .Output(SourcesComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static MethodInfo GetMethod(string name)
        => typeof(SourceNodeFactory).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find source factory method '{name}'.");
}
