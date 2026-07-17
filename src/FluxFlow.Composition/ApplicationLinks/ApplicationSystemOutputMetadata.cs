using FluxFlow.Composition.Addressing;

namespace FluxFlow.Composition.Links;

public sealed record ApplicationSystemOutputMetadata
{
    public ApplicationSystemOutputMetadata(ApplicationAddress address, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Kind != ApplicationAddressKind.SystemPort)
        {
            throw new ArgumentException(
                $"Address '{address}' is not a reserved system output.",
                nameof(address));
        }

        Address = address;
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
    }

    public ApplicationAddress Address { get; }

    public Type MessageType { get; }

    public static ApplicationSystemOutputMetadata Create<TMessage>(ApplicationAddress address)
        => new(address, typeof(TMessage));
}
