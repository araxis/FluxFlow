using FluxFlow.Components.Timers.Options;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Components.Timers.Nodes;

internal static class TimerTypedNodeFactory
{
    public static RuntimeNode Create<TSettings>(
        RuntimeNodeFactoryContext context,
        TimerComponentOptions componentOptions,
        Func<NodeDefinition, TSettings> readSettings,
        MethodInfo createTypedMethod)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);
        ArgumentNullException.ThrowIfNull(readSettings);
        ArgumentNullException.ThrowIfNull(createTypedMethod);

        var settings = readSettings(context.Definition);
        var inputType = componentOptions.ResolveType(ResolveInputType(settings));

        try
        {
            var method = createTypedMethod.MakeGenericMethod(inputType);
            return (RuntimeNode)method.Invoke(null, [context, settings, componentOptions.Clock])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public static MethodInfo GetCreateMethod(Type factoryType, string methodName)
    {
        ArgumentNullException.ThrowIfNull(factoryType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);

        return factoryType.GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not find timer factory method '{methodName}'.");
    }

    private static string ResolveInputType<TSettings>(TSettings settings)
        => settings switch
        {
            TimerDebounceSettings debounce => debounce.InputType,
            TimerDelaySettings delay => delay.InputType,
            TimerThrottleSettings throttle => throttle.InputType,
            _ => throw new InvalidOperationException(
                $"Timer settings type '{typeof(TSettings).Name}' does not expose an input type.")
        };
}
