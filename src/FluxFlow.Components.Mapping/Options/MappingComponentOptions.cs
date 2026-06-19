using FluxFlow.Components.Expressions;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Mapping;
using System.Text.Json;

namespace FluxFlow.Components.Mapping.Options;

public sealed class MappingComponentOptions
{
    private readonly object _typesLock = new();
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [MapperOptions.ObjectTypeName] = typeof(object),
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

    private readonly FlowExpressionEngineRegistry _expressionEngines = new("Mapping");
    private readonly FlowContextFactoryRegistry<IMappingContextFactory> _contextFactories =
        new(new DefaultMappingContextFactory());

    public MappingComponentOptions UseExpressionEngine(
        IFlowExpressionEngine expressionEngine,
        bool useAsDefault = true)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        ArgumentException.ThrowIfNullOrWhiteSpace(expressionEngine.Name);

        _expressionEngines.Use(expressionEngine, useAsDefault);
        return this;
    }

    public MappingComponentOptions UseExpressionEngineResolver(
        Func<string?, IFlowExpressionEngine> resolver)
    {
        _expressionEngines.UseResolver(resolver);
        return this;
    }

    public MappingComponentOptions RegisterType<T>(string name)
        => RegisterType(name, typeof(T));

    public MappingComponentOptions RegisterType(string name, Type type)
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

    public MappingComponentOptions UseDefaultContextFactory(IMappingContextFactory contextFactory)
    {
        _contextFactories.UseDefault(contextFactory);
        return this;
    }

    public MappingComponentOptions UseContextFactory<TInput>(IFlowMapContextFactory<TInput> contextFactory)
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
            $"Mapping type '{name}' is not registered.");
    }

    internal IMappingContextFactory ResolveContextFactory(Type inputType)
    {
        ArgumentNullException.ThrowIfNull(inputType);

        return _contextFactories.Resolve(inputType);
    }

    private sealed class TypedContextFactory<TInput>(IFlowMapContextFactory<TInput> inner)
        : IMappingContextFactory
    {
        public FlowMapContext Create(object? input, MappingNodeContext context)
            => input is TInput typedInput
                ? inner.Create(typedInput)
                : throw new InvalidOperationException(
                    $"Mapping context expected input type '{typeof(TInput).Name}'.");
    }

    private sealed class DefaultMappingContextFactory : IMappingContextFactory
    {
        public FlowMapContext Create(object? input, MappingNodeContext context)
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
