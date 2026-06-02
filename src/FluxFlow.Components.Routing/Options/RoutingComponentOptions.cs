using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Engine.Mapping;
using System.Text.Json;

namespace FluxFlow.Components.Routing.Options;

public sealed class RoutingComponentOptions
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [SwitchRoutingOptions.ObjectTypeName] = typeof(object),
        [typeof(object).FullName!] = typeof(object),
        ["string"] = typeof(string),
        [typeof(string).FullName!] = typeof(string),
        ["bool"] = typeof(bool),
        [typeof(bool).FullName!] = typeof(bool),
        ["int"] = typeof(int),
        [typeof(int).FullName!] = typeof(int),
        ["long"] = typeof(long),
        [typeof(long).FullName!] = typeof(long),
        ["double"] = typeof(double),
        [typeof(double).FullName!] = typeof(double),
        ["decimal"] = typeof(decimal),
        [typeof(decimal).FullName!] = typeof(decimal),
        ["bytes"] = typeof(byte[]),
        [typeof(byte[]).FullName!] = typeof(byte[]),
        ["json"] = typeof(JsonElement),
        [nameof(JsonElement)] = typeof(JsonElement),
        [typeof(JsonElement).FullName!] = typeof(JsonElement)
    };

    private readonly Dictionary<string, IFlowExpressionEngine> _expressionEngines =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, IRoutingContextFactory> _contextFactories = [];
    private IFlowExpressionEngine? _defaultExpressionEngine;
    private Func<string?, IFlowExpressionEngine>? _expressionEngineResolver;
    private IRoutingContextFactory _defaultContextFactory = new DefaultRoutingContextFactory();

    public RoutingComponentOptions UseExpressionEngine(
        IFlowExpressionEngine expressionEngine,
        bool useAsDefault = true)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        ArgumentException.ThrowIfNullOrWhiteSpace(expressionEngine.Name);

        _expressionEngines[expressionEngine.Name] = expressionEngine;
        if (useAsDefault || _defaultExpressionEngine is null)
        {
            _defaultExpressionEngine = expressionEngine;
        }

        return this;
    }

    public RoutingComponentOptions UseExpressionEngineResolver(
        Func<string?, IFlowExpressionEngine> resolver)
    {
        _expressionEngineResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    public RoutingComponentOptions RegisterType<T>(string name)
        => RegisterType(name, typeof(T));

    public RoutingComponentOptions RegisterType(string name, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        _types[name.Trim()] = type;
        _types[type.FullName ?? type.Name] = type;
        return this;
    }

    public RoutingComponentOptions UseDefaultContextFactory(IRoutingContextFactory contextFactory)
    {
        _defaultContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        return this;
    }

    public RoutingComponentOptions UseContextFactory<TInput>(IFlowMapContextFactory<TInput> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactories[typeof(TInput)] = new TypedContextFactory<TInput>(contextFactory);
        return this;
    }

    internal IFlowExpressionEngine ResolveExpressionEngine(string? name)
    {
        if (_expressionEngineResolver is not null)
        {
            var resolved = _expressionEngineResolver(name);
            return resolved ?? throw new InvalidOperationException(
                "Routing expression engine resolver returned null.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return _defaultExpressionEngine ?? throw new InvalidOperationException(
                "Routing components require an expression engine.");
        }

        if (_expressionEngines.TryGetValue(name.Trim(), out var expressionEngine))
        {
            return expressionEngine;
        }

        throw new InvalidOperationException(
            $"Routing expression engine '{name}' is not registered.");
    }

    internal Type ResolveType(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = name.Trim();

        if (_types.TryGetValue(key, out var type))
        {
            return type;
        }

        var resolved = Type.GetType(key, throwOnError: false, ignoreCase: false);
        if (resolved is not null)
        {
            _types[key] = resolved;
            return resolved;
        }

        throw new InvalidOperationException(
            $"Routing input type '{name}' is not registered.");
    }

    internal IRoutingContextFactory ResolveContextFactory(Type inputType)
    {
        ArgumentNullException.ThrowIfNull(inputType);

        if (_contextFactories.TryGetValue(inputType, out var exact))
        {
            return exact;
        }

        foreach (var (candidateType, factory) in _contextFactories)
        {
            if (candidateType.IsAssignableFrom(inputType))
            {
                return factory;
            }
        }

        return _defaultContextFactory;
    }

    private sealed class TypedContextFactory<TInput>(IFlowMapContextFactory<TInput> inner)
        : IRoutingContextFactory
    {
        public FlowMapContext Create(object? input, RoutingNodeContext context)
            => input is TInput typedInput
                ? inner.Create(typedInput)
                : throw new InvalidOperationException(
                    $"Routing context expected input type '{typeof(TInput).Name}'.");
    }

    private sealed class DefaultRoutingContextFactory : IRoutingContextFactory
    {
        public FlowMapContext Create(object? input, RoutingNodeContext context)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input
                }
            };
    }
}
