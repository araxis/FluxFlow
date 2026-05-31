namespace FluxFlow.Engine;

public sealed class FlowApplicationConfigurationException : Exception
{
    public FlowApplicationConfigurationException(string message)
        : base(message)
    {
    }

    public FlowApplicationConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
