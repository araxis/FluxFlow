namespace FluxFlow.Environments;

public interface IEnvironmentProvider
{
    string Name { get; }
    string ResourceType { get; }
    void Validate(ResourceBinding binding);
    Task<IEnvironmentService> StartAsync(ResourceBinding binding, CancellationToken cancellationToken);
}

public interface IEnvironmentService : IAsyncDisposable
{
    Uri Endpoint { get; }
    string? ContainerId { get; }
}
