namespace FluxFlow.Engine.Runtime;

public enum ApplicationRuntimeBuildErrorCode
{
    ValidationFailed = 1,
    UnknownNodeType = 2,
    FactoryFailed = 3,
    MissingInputPort = 4,
    MissingOutputPort = 5,
    PortTypeMismatch = 6,
    LinkFailed = 7
}
