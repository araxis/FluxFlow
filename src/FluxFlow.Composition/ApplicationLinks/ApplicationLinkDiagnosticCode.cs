namespace FluxFlow.Composition.Links;

public enum ApplicationLinkDiagnosticCode
{
    UnknownComponentType,
    AmbiguousPortProperty,
    InvalidLinkDeclaration,
    InvalidPortReference,
    MissingComponent,
    MissingInputPort,
    MissingOutputPort,
    MissingSystemOutputMetadata,
    DuplicateLink,
    PortTypeMismatch,
    MissingConditionEngine,
    InvalidCondition,
    ExclusivePortClaim,
    CycleDetected
}
