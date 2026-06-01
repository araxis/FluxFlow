using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Engine.Mapping;
using System.Text.Json;

namespace FluxFlow.Components.Assertions.Options;

public sealed class AssertionsComponentOptions
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [AssertionOptions.ObjectTypeName] = typeof(object),
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
    private readonly Dictionary<Type, IAssertionContextFactory> _contextFactories = [];
    private IFlowExpressionEngine? _defaultExpressionEngine;
    private Func<string?, IFlowExpressionEngine>? _expressionEngineResolver;
    private IAssertionContextFactory _defaultContextFactory = new DefaultAssertionContextFactory();

    public AssertionsComponentOptions UseExpressionEngine(
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

    public AssertionsComponentOptions UseExpressionEngineResolver(
        Func<string?, IFlowExpressionEngine> resolver)
    {
        _expressionEngineResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    public AssertionsComponentOptions RegisterType<T>(string name)
        => RegisterType(name, typeof(T));

    public AssertionsComponentOptions RegisterType(string name, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        _types[name.Trim()] = type;
        _types[type.FullName ?? type.Name] = type;
        return this;
    }

    public AssertionsComponentOptions UseDefaultContextFactory(IAssertionContextFactory contextFactory)
    {
        _defaultContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        return this;
    }

    public AssertionsComponentOptions UseContextFactory<TInput>(IFlowMapContextFactory<TInput> contextFactory)
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
                "Assertion expression engine resolver returned null.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return _defaultExpressionEngine ?? throw new InvalidOperationException(
                "Assertion components require an expression engine.");
        }

        if (_expressionEngines.TryGetValue(name.Trim(), out var expressionEngine))
        {
            return expressionEngine;
        }

        throw new InvalidOperationException(
            $"Assertion expression engine '{name}' is not registered.");
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
            $"Assertion input type '{name}' is not registered.");
    }

    internal IAssertionContextFactory ResolveContextFactory(Type inputType)
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
        : IAssertionContextFactory
    {
        public FlowMapContext Create(object? input, AssertionNodeContext context)
            => input is TInput typedInput
                ? inner.Create(typedInput)
                : throw new InvalidOperationException(
                    $"Assertion context expected input type '{typeof(TInput).Name}'.");
    }

    private sealed class DefaultAssertionContextFactory : IAssertionContextFactory
    {
        public FlowMapContext Create(object? input, AssertionNodeContext context)
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
