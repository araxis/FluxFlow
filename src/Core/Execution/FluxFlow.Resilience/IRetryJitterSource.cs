namespace FluxFlow.Resilience;

public interface IRetryJitterSource
{
    double NextSample();
}

public sealed class RandomRetryJitterSource : IRetryJitterSource
{
    public static RandomRetryJitterSource Shared { get; } = new();

    private RandomRetryJitterSource()
    {
    }

    public double NextSample() => Random.Shared.NextDouble();
}
