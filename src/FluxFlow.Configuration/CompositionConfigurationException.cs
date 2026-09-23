namespace FluxFlow.Configuration;

public sealed class CompositionConfigurationException : Exception
{
    public CompositionConfigurationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
