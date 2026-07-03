using FluxFlow.Components.Secrets.Contracts;

namespace FluxFlow.Components.Secrets;

public interface ISecretDescriptorProvider
{
    IReadOnlyCollection<SecretDescriptor> GetDescriptors();
}
