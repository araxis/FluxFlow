using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Timing;
using System.Text.Json;

namespace FluxFlow.Components.Validation.Options;

public sealed class ValidationComponentOptions
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [JsonSchemaValidatorOptions.ObjectTypeName] = typeof(object),
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

    private readonly Dictionary<SelectorKey, IValidationValueSelector> _selectors = [];
    private IValidationClock _clock = SystemValidationClock.Instance;

    public IValidationClock Clock => _clock;

    public ValidationComponentOptions UseClock(IValidationClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    public ValidationComponentOptions RegisterType<T>(string name)
        => RegisterType(name, typeof(T));

    public ValidationComponentOptions RegisterType(string name, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        _types[name.Trim()] = type;
        _types[type.FullName ?? type.Name] = type;
        return this;
    }

    public ValidationComponentOptions UseValueSelector<TInput>(
        string name,
        IJsonSchemaValueSelector<TInput> selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(selector);

        _selectors[new SelectorKey(typeof(TInput), name.Trim())] =
            new TypedValueSelector<TInput>(selector);
        return this;
    }

    public ValidationComponentOptions UseValueSelector<TInput>(
        string name,
        Func<TInput, JsonSchemaValidatorContext, object?> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return UseValueSelector(name, new DelegateValueSelector<TInput>(selector));
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
            $"Validation type '{name}' is not registered.");
    }

    internal IValidationValueSelector ResolveValueSelector(Type inputType, string name)
    {
        ArgumentNullException.ThrowIfNull(inputType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (IsDefaultSelector(name))
        {
            return DefaultValueSelector.Instance;
        }

        if (_selectors.TryGetValue(new SelectorKey(inputType, name.Trim()), out var exact))
        {
            return exact;
        }

        foreach (var (key, selector) in _selectors)
        {
            if (key.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                key.InputType.IsAssignableFrom(inputType))
            {
                return selector;
            }
        }

        throw new InvalidOperationException(
            $"Validation value selector '{name}' is not registered for '{inputType.Name}'.");
    }

    private static bool IsDefaultSelector(string name)
        => name.Equals(JsonSchemaValidatorOptions.DefaultValueSelector, StringComparison.OrdinalIgnoreCase) ||
           name.Equals("value", StringComparison.OrdinalIgnoreCase);

    private sealed record SelectorKey(Type InputType, string Name);

    internal interface IValidationValueSelector
    {
        object? Select(object? input, JsonSchemaValidatorContext context);
    }

    private sealed class TypedValueSelector<TInput>(IJsonSchemaValueSelector<TInput> inner)
        : IValidationValueSelector
    {
        public object? Select(object? input, JsonSchemaValidatorContext context)
            => input is TInput typedInput
                ? inner.Select(typedInput, context)
                : throw new InvalidOperationException(
                    $"Validation selector expected input type '{typeof(TInput).Name}'.");
    }

    private sealed class DelegateValueSelector<TInput>(
        Func<TInput, JsonSchemaValidatorContext, object?> selector)
        : IJsonSchemaValueSelector<TInput>
    {
        public object? Select(TInput input, JsonSchemaValidatorContext context)
            => selector(input, context);
    }

    private sealed class DefaultValueSelector : IValidationValueSelector
    {
        public static DefaultValueSelector Instance { get; } = new();

        public object? Select(object? input, JsonSchemaValidatorContext context)
            => input;
    }
}
