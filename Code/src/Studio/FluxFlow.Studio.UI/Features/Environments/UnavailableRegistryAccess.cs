using FluxFlow.Environments;

namespace FluxFlow.Studio.UI.Features.Environments;

internal sealed class UnavailableRegistryAccess : IRegistryAccess
{
    private static Task<RegistryAccessResult> Unavailable() => Task.FromResult(new RegistryAccessResult(false, "This host has not configured local registry access."));
    public Task<RegistryAccessResult> TestAsync(ImageRegistry registry, CancellationToken cancellationToken) => Unavailable();
    public Task<RegistryAccessResult> SignInAsync(RegistrySignIn request, CancellationToken cancellationToken) => Unavailable();
    public Task<RegistryAccessResult> SignOutAsync(ImageRegistry registry, CancellationToken cancellationToken) => Unavailable();
}
