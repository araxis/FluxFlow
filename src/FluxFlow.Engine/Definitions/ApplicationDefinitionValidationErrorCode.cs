namespace FluxFlow.Engine.Definitions;

public enum ApplicationDefinitionValidationErrorCode
{
    EmptyDefinition = 1,
    EmptyWorkflowName = 2,
    EmptyWorkflow = 3,
    EmptyNodeName = 4,
    EmptyResourceName = 5,
    EmptyNodeType = 6,
    InvalidLink = 7,
    MissingSourceNode = 8,
    EmptySourcePort = 9,
    EmptyTargetPort = 10,
    DuplicateLink = 11,
    EmptyScenarioName = 19,
    EmptyScenarioStepName = 20,
    EmptyScenarioStepType = 21,
    UnknownScenarioStepType = 22,
    InvalidScenarioStepConfiguration = 23
}
