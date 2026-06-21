namespace FluxFlow.Composition;

public enum CompositionDiagnosticCode
{
    EmptyDefinition,
    EmptyWorkflowName,
    EmptyWorkflow,
    EmptyNodeName,
    EmptyNodeType,
    UnknownNodeType,
    MissingNode,
    MissingInputPort,
    MissingOutputPort,
    DuplicateLink,
    PortTypeMismatch,
    FactoryFailed,
    DescriptorPortMismatch,
    LinkFailed,
    CleanupFailed,
    InvalidConfiguration
}
