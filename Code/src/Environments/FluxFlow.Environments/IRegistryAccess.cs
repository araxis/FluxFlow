namespace FluxFlow.Environments;

public sealed record RegistrySignIn(ImageRegistry Registry, string Username, string Password);
public sealed record RegistryAccessResult(bool Success, string Message);

public interface IRegistryAccess
{
    Task<RegistryAccessResult> TestAsync(ImageRegistry registry, CancellationToken cancellationToken);
    Task<RegistryAccessResult> SignInAsync(RegistrySignIn request, CancellationToken cancellationToken);
    Task<RegistryAccessResult> SignOutAsync(ImageRegistry registry, CancellationToken cancellationToken);
}
