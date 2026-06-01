using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Engine.Mapping;
using System.Text.Json;

namespace FluxFlow.Components.Observability.Options;

public sealed class ObservabilityComponentOptions
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [FlowCounterOptions.ObjectTypeName] = typeof(object),
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
    private readonly Dictionary<Type, IObservabilityContextFactory> _contextFactories = [];
    private readonly Dictionary<SelectorKey, IValueSelector> _valueSelectors = [];
    private IFlowExpressionEngine? _defaultExpressionEngine;
    private Func<string?, IFlowExpressionEngine>? _expressionEngineResolver;
    private IObservabilityContextFactory _defaultContextFactory = new DefaultObservabilityContextFactory();

    public ObservabilityComponentOptions UseExpressionEngine(
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

    public ObservabilityComponentOptions UseExpressionEngineResolver(
        Func<string?, IFlowExpressionEngine> resolver)
    {
        _expressionEngineResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    public ObservabilityComponentOptions RegisterType<T>(string name)
        => RegisterType(name, typeof(T));

    public ObservabilityComponentOptions RegisterType(string name, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        _types[name.Trim()] = type;
        _types[type.FullName ?? type.Name] = type;
        return this;
    }

    public ObservabilityComponentOptions UseDefaultContextFactory(
        IObservabilityContextFactory contextFactory)
    {
        _defaultContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        return this;
    }

    public ObservabilityComponentOptions UseContextFactory<TInput>(
        IFlowMapContextFactory<TInput> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactories[typeof(TInput)] = new TypedContextFactory<TInput>(contextFactory);
        return this;
    }

    public ObservabilityComponentOptions UseValueSelector<TInput>(
        string name,
        IObservabilityValueSelector<TInput> selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(selector);

        _valueSelectors[new SelectorKey(typeof(TInput), name.Trim())] =
            new TypedValueSelector<TInput>(selector);
        return this;
    }

    public ObservabilityComponentOptions UseValueSelector<TInput>(
        string name,
        Func<TInput, ObservabilityNodeContext, object?> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return UseValueSelector(name, new DelegateValueSelector<TInput>(selector));
    }

    internal IFlowExpressionEngine ResolveExpressionEngine(string? name)
    {
        if (_expressionEngineResolver is not null)
        {
            var resolved = _expressionEngineResolver(name);
            return resolved ?? throw new InvalidOperationException(
                "Observability expression engine resolver returned null.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return _defaultExpressionEngine ?? throw new InvalidOperationException(
                "Observability components require an expression engine for predicates.");
        }

        if (_expressionEngines.TryGetValue(name.Trim(), out var expressionEngine))
        {
            return expressionEngine;
        }

        throw new InvalidOperationException(
            $"Observability expression engine '{name}' is not registered.");
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
            $"Observability type '{name}' is not registered.");
    }

    internal IObservabilityContextFactory ResolveContextFactory(Type inputType)
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

    internal IValueSelector? ResolveOptionalValueSelector(Type inputType, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return ResolveValueSelector(inputType, name);
    }

    internal IValueSelector ResolveValueSelector(Type inputType, string name)
    {
        ArgumentNullException.ThrowIfNull(inputType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (IsDefaultSelector(name))
        {
            return DefaultValueSelector.Instance;
        }

        if (_valueSelectors.TryGetValue(new SelectorKey(inputType, name.Trim()), out var exact))
        {
            return exact;
        }

        foreach (var (key, selector) in _valueSelectors)
        {
            if (key.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                key.InputType.IsAssignableFrom(inputType))
            {
                return selector;
            }
        }

        throw new InvalidOperationException(
            $"Observability value selector '{name}' is not registered for '{inputType.Name}'.");
    }

    private static bool IsDefaultSelector(string name)
        => name.Equals("input", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("value", StringComparison.OrdinalIgnoreCase);

    private sealed record SelectorKey(Type InputType, string Name);

    internal interface IValueSelector
    {
        object? Select(object? input, ObservabilityNodeContext context);
    }

    private sealed class TypedContextFactory<TInput>(IFlowMapContextFactory<TInput> inner)
        : IObservabilityContextFactory
    {
        public FlowMapContext Create(object? input, ObservabilityNodeContext context)
            => input is TInput typedInput
                ? inner.Create(typedInput)
                : throw new InvalidOperationException(
                    $"Observability context expected input type '{typeof(TInput).Name}'.");
    }

    private sealed class DefaultObservabilityContextFactory : IObservabilityContextFactory
    {
        public FlowMapContext Create(object? input, ObservabilityNodeContext context)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input
                }
            };
    }

    private sealed class TypedValueSelector<TInput>(IObservabilityValueSelector<TInput> inner)
        : IValueSelector
    {
        public object? Select(object? input, ObservabilityNodeContext context)
            => input is TInput typedInput
                ? inner.Select(typedInput, context)
                : throw new InvalidOperationException(
                    $"Observability selector expected input type '{typeof(TInput).Name}'.");
    }

    private sealed class DelegateValueSelector<TInput>(
        Func<TInput, ObservabilityNodeContext, object?> selector)
        : IObservabilityValueSelector<TInput>
    {
        public object? Select(TInput input, ObservabilityNodeContext context)
            => selector(input, context);
    }

    private sealed class DefaultValueSelector : IValueSelector
    {
        public static DefaultValueSelector Instance { get; } = new();

        public object? Select(object? input, ObservabilityNodeContext context)
            => input;
    }
}
