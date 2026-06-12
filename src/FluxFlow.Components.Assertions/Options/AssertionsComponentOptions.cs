using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Timing;
using FluxFlow.Components.Expressions;
using FluxFlow.Engine.Mapping;
using System.Text.Json;

namespace FluxFlow.Components.Assertions.Options;

public sealed class AssertionsComponentOptions
{
    private readonly object _typesLock = new();
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

    private readonly FlowExpressionEngineRegistry _expressionEngines = new("Assertion");
    private readonly FlowContextFactoryRegistry<IAssertionContextFactory> _contextFactories =
        new(new DefaultAssertionContextFactory());
    private IAssertionClock _clock = SystemAssertionClock.Instance;

    public IAssertionClock Clock => _clock;

    public AssertionsComponentOptions UseClock(IAssertionClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    public AssertionsComponentOptions UseExpressionEngine(
        IFlowExpressionEngine expressionEngine,
        bool useAsDefault = true)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        ArgumentException.ThrowIfNullOrWhiteSpace(expressionEngine.Name);

        _expressionEngines.Use(expressionEngine, useAsDefault);
        return this;
    }

    public AssertionsComponentOptions UseExpressionEngineResolver(
        Func<string?, IFlowExpressionEngine> resolver)
    {
        _expressionEngines.UseResolver(resolver);
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
        _contextFactories.UseDefault(contextFactory);
        return this;
    }

    public AssertionsComponentOptions UseContextFactory<TInput>(IFlowMapContextFactory<TInput> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactories.Register(typeof(TInput), new TypedContextFactory<TInput>(contextFactory));
        return this;
    }

    internal IFlowExpressionEngine ResolveExpressionEngine(string? name)
        => _expressionEngines.Resolve(name);

    internal Type ResolveType(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = name.Trim();

        lock (_typesLock)
        {
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
        }

        throw new InvalidOperationException(
            $"Assertion input type '{name}' is not registered.");
    }

    internal IAssertionContextFactory ResolveContextFactory(Type inputType)
    {
        ArgumentNullException.ThrowIfNull(inputType);

        return _contextFactories.Resolve(inputType);
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
