using FluxFlow.Composition.Addressing;

namespace FluxFlow.Components.Resources.Contracts;

public readonly record struct ResourceName
{
    public ResourceName(string value)
    {
        try
        {
            var address = ApplicationAddress.Parse(value);
            if (address.Kind != ApplicationAddressKind.Resource)
            {
                throw new ArgumentException(
                    $"Address '{address}' is not a resource address.",
                    nameof(value));
            }

            Value = address.Value;
        }
        catch (ArgumentException exception) when (exception.ParamName != nameof(value))
        {
            throw new ArgumentException(
                "Resource names must be canonical application resource addresses.",
                nameof(value),
                exception);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Resource names must be canonical application resource addresses.",
                nameof(value),
                exception);
        }
    }

    public ResourceName(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Kind != ApplicationAddressKind.Resource)
        {
            throw new ArgumentException(
                $"Address '{address}' is not a resource address.",
                nameof(address));
        }

        Value = address.Value;
    }

    public string Value { get; }

    public ApplicationAddress Address => ApplicationAddress.Parse(Value);

    public override string ToString() => Value ?? string.Empty;
}
