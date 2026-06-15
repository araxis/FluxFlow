using FluxFlow.Components.Control.Contracts;
using FluxFlow.Components.Expressions;
using FluxFlow.Engine.Mapping;
using System.Text.Json;

namespace FluxFlow.Components.Control.Options;

public sealed class ControlComponentOptions
{
    private readonly object _typesLock = new();
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [ControlExpressionOptions.ObjectTypeName] = typeof(object),
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

    private readonly FlowExpressionEngineRegistry _expressionEngines = new("Control");
    private readonly FlowContextFactoryRegistry<IControlContextFactory> _contextFactories =
        new(new DefaultControlContextFactory());

    public ControlComponentOptions UseExpressionEngine(
        IFlowExpressionEngine expressionEngine,
        bool useAsDefault = true)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        ArgumentException.ThrowIfNullOrWhiteSpace(expressionEngine.Name);

        _expressionEngines.Use(expressionEngine, useAsDefault);
        return this;
    }

    public ControlComponentOptions UseExpressionEngineResolver(
        Func<string?, IFlowExpressionEngine> resolver)
    {
        _expressionEngines.UseResolver(resolver);
        return this;
    }

    public ControlComponentOptions RegisterType<T>(string name)
        => RegisterType(name, typeof(T));

    public ControlComponentOptions RegisterType(string name, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        lock (_typesLock)
        {
            _types[name.Trim()] = type;
            _types[type.FullName ?? type.Name] = type;
        }

        return this;
    }

    public ControlComponentOptions UseDefaultContextFactory(IControlContextFactory contextFactory)
    {
        _contextFactories.UseDefault(contextFactory);
        return this;
    }

    public ControlComponentOptions UseContextFactory<TInput>(IFlowMapContextFactory<TInput> contextFactory)
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
            $"Control type '{name}' is not registered.");
    }

    internal IControlContextFactory ResolveContextFactory(Type inputType)
    {
        ArgumentNullException.ThrowIfNull(inputType);

        return _contextFactories.Resolve(inputType);
    }

    private sealed class TypedContextFactory<TInput>(IFlowMapContextFactory<TInput> inner)
        : IControlContextFactory
    {
        public FlowMapContext Create(object? input, ControlNodeContext context)
            => input is TInput typedInput
                ? inner.Create(typedInput)
                : throw new InvalidOperationException(
                    $"Control context expected input type '{typeof(TInput).Name}'.");
    }

    private sealed class DefaultControlContextFactory : IControlContextFactory
    {
        public FlowMapContext Create(object? input, ControlNodeContext context)
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
