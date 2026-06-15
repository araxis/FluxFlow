using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Components.Validation.Timing;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Validation.Nodes;

internal static class JsonSchemaValidatorNodeFactory
{
    private static readonly MethodInfo CreateTypedMethod =
        typeof(JsonSchemaValidatorNodeFactory).GetMethod(
            nameof(CreateTyped),
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find typed validator factory method.");

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        ValidationComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = ValidationOptionsReader.ReadJsonSchemaValidatorOptions(context.Definition);
        var inputType = componentOptions.ResolveType(options.InputType);
        var selector = componentOptions.ResolveValueSelector(
            inputType,
            options.EffectiveValueSelector);
        var nodeContext = new JsonSchemaValidatorContext
        {
            Address = context.Address,
            Options = options,
            InputType = inputType
        };

        try
        {
            var method = CreateTypedMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(
                null,
                [context, options, selector, nodeContext, componentOptions.Clock])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static RuntimeNode CreateTyped<TInput>(
        RuntimeNodeFactoryContext context,
        JsonSchemaValidatorOptions options,
        ValidationComponentOptions.IValidationValueSelector selector,
        JsonSchemaValidatorContext nodeContext,
        IValidationClock clock)
    {
        var node = new JsonSchemaValidatorNode<TInput>(
            options,
            selector,
            nodeContext,
            clock);

        return context.CreateNode(node)
            .Input(ValidationComponentPorts.Input, node.Input)
            .Output(ValidationComponentPorts.Result, node.Result)
            .Output(ValidationComponentPorts.Valid, node.Valid)
            .Output(ValidationComponentPorts.Invalid, node.Invalid)
            .Output(ValidationComponentPorts.Errors, node.Errors)
            .Build();
    }
}
