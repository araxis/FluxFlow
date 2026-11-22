namespace FluxFlow.Configuration.Application;

public sealed class CompositionConfigurationException(string message, Exception? innerException = null) : Exception(message, innerException);