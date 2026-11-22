namespace FluxFlow.Environments;

public sealed record EnvironmentCheckResult(bool Success, string Message)
{
    public string? NodeId { get; init; }
    public string? Resource { get; init; }
}

public interface IContainerRuntimeCheck
{
    Task CheckAsync(CancellationToken cancellationToken);
}

public sealed class EnvironmentCheck(EnvironmentPreparation preparation, IEnumerable<IContainerRuntimeCheck> runtimes)
{
    public async Task<EnvironmentCheckResult> CheckAsync(EnvironmentRunRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = preparation.CheckValidation(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is not null) return failure;

        cancellationToken.ThrowIfCancellationRequested();
        if (!preparation.PreparationProfile(request).Resources.Values.Any(binding => binding.Provider != "existing"))
            return new(true, "Setup is valid. Docker is not required. Existing service connectivity was not tested.");

        var runtime = runtimes.SingleOrDefault();
        if (runtime is null) return new(false, "This companion has no container runtime configured.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await runtime.CheckAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            return new(true, "Setup is valid and Docker is available. Images, registry access and service connectivity were not tested.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, "Docker did not respond within 10 seconds. Check Docker on the companion computer."); }
        catch (Exception error) when (error is not OperationCanceledException)
        { cancellationToken.ThrowIfCancellationRequested(); return new(false, $"Docker is unavailable: {error.Message}"); }
    }
}
