# API reference

Complete listing of every public type in `FluxFlow.Engine`.

---

## FluxFlow.Engine.Core

### `FlowNodeId`
```
readonly record struct FlowNodeId(Guid Value)
  static FlowNodeId New()
  static FlowNodeId Empty
  override string ToString()
```

---

## FluxFlow.Engine.Components

### `IFlowNode : IDataflowBlock`
```
FlowNodeId          Id        { get; }
ISourceBlock<FlowError> Errors { get; }
Task StartAsync(CancellationToken ct = default)   // default: Task.CompletedTask
// From IDataflowBlock:
Task Completion { get; }
void Complete()
void Fault(Exception exception)
```

### `IFlowEventSource`
```
ISourceBlock<FlowEvent> Events { get; }
```

### `FlowError`
```
sealed record FlowError
  required FlowNodeId NodeId
  required int        Code
  required string     Message
  Exception?          Exception       = null
  DateTimeOffset      OccurredAt      = DateTimeOffset.UtcNow
  string?             Context         = null
```

### `FlowErrorCodes`
```
static class FlowErrorCodes
  const int NodeFaulted              = 1000
  const int ProcessingFailed         = 2000
  const int DynamicExpressionFailed  = 3000
```

### `FlowEvent`
```
sealed record FlowEvent
  required DateTimeOffset Timestamp
  required string         Type
  required string         Source
  FlowNodeId?             SourceNodeId    = null
  string?                 Subject         = null
  string?                 Status          = null
  string?                 Topic           = null
  int?                    PayloadBytes    = null
  string?                 PayloadPreview  = null
  IReadOnlyDictionary<string,string> Attributes = {}
  string? GetAttribute(string name)
```

### `FlowEventTypes`
```
static class FlowEventTypes
  const string MqttMessageReceived   = "mqtt.message.received"
  const string MqttMessagePublished  = "mqtt.message.published"
  const string MqttMessageRecorded   = "mqtt.message.recorded"
  const string FileWritten           = "file.written"
  const string JsonSchemaValidated   = "json.schema.validated"
  const string AssertionEvaluated    = "flow.assertion.evaluated"
```

---

## FluxFlow.Engine.Definitions

### `ApplicationDefinition`
```
sealed record ApplicationDefinition
  Dictionary<string,NodeDefinition>      Resources   = {}
  Dictionary<string,WorkflowDefinition>  Workflows   = {}
  Dictionary<string,DashboardDefinition> Dashboards  = {}
  Dictionary<string,ScenarioDefinition>  Tests       = {}
```

### `WorkflowDefinition`
```
sealed record WorkflowDefinition
  Dictionary<string,NodeDefinition> Nodes = {}
```

### `NodeDefinition`
```
sealed record NodeDefinition
  required NodeType                   Type
  Dictionary<string,JsonElement>      Configuration = {}
  string?                             When          = null
  int                                 Phase         = 0
  Dictionary<string,JsonElement>      Ports         = {}    // [JsonExtensionData]
  IReadOnlyList<LinkDefinition> GetPortLinks(string portName, string workflowName)
  IReadOnlyDictionary<string,IReadOnlyList<LinkDefinition>> GetAllPortLinks(string workflowName)
```

### `LinkDefinition`
```
sealed record LinkDefinition
  required PortAddress From
  string?              When = null
```

### `NodeType`
```
readonly record struct NodeType(string Value)
```

### `NodeName`
```
readonly record struct NodeName(string Value)
```

### `PortName`
```
readonly record struct PortName(string Value)
```

### `WorkflowName`
```
readonly record struct WorkflowName(string Value)
```

### `NodeAddress`
```
readonly record struct NodeAddress(string Scope, NodeName Node)
  PortAddress Port(PortName port)
```

### `PortAddress`
```
readonly record struct PortAddress(string Scope, NodeName Node, PortName Port)
  static PortAddress Parse(string value)
  override string ToString()   // "scope.node.port"
```

### `WellKnownScopes`
```
static class WellKnownScopes
  const string Resources = "resources"
```

### `ApplicationDefinitionValidator`
```
class ApplicationDefinitionValidator
  ApplicationDefinitionValidationResult Validate(ApplicationDefinition definition)
```

### `ApplicationDefinitionValidationResult`
```
sealed record ApplicationDefinitionValidationResult(IReadOnlyList<ApplicationDefinitionValidationError> Errors)
  bool IsValid => Errors.Count == 0
```

### `ApplicationDefinitionValidationError`
```
sealed record ApplicationDefinitionValidationError(
  ApplicationDefinitionValidationErrorCode Code,
  string  Message,
  string? WorkflowName = null,
  string? NodeName     = null,
  string? PortName     = null)
```

### `ApplicationDefinitionValidationErrorCode`
```
enum ApplicationDefinitionValidationErrorCode
  EmptyDefinition, EmptyWorkflow, EmptyNodeName, EmptyNodeType,
  EmptyResourceName, EmptyWorkflowName, EmptyTargetPort, EmptySourcePort,
  MissingSourceNode, DuplicateLink, InvalidLink,
  EmptyScenarioName, EmptyScenarioStepName, EmptyScenarioStepType,
  UnknownScenarioStepType, MissingScenarioStepResource, InvalidScenarioStepConfiguration,
  EmptyDashboardName, InvalidDashboardLayout, InvalidDashboardCell,
  EmptyDashboardWidgetName, EmptyDashboardWidgetType, MissingDashboardWidget,
  EmptyDashboardCellName
```

### `ApplicationDefinitionJson`
```
static class ApplicationDefinitionJson
  static JsonSerializerOptions CreateSerializerOptions()
  // Use these options for all Serialize/Deserialize calls
```

### `ScenarioDefinition`
```
sealed record ScenarioDefinition
  Dictionary<string,ScenarioStepDefinition> Steps = {}
```

---

## FluxFlow.Engine.Runtime

### `RuntimeNodeFactoryRegistry`
```
class RuntimeNodeFactoryRegistry
  RuntimeNodeFactoryRegistry Register(NodeType type, RuntimeNodeFactory factory)
  RuntimeNodeFactoryRegistry Register(NodeType type, Func<NodeAddress,NodeDefinition,RuntimeNode> factory)
  bool TryGetFactory(NodeType type, out RuntimeNodeFactory factory)
  IReadOnlyDictionary<NodeType,RuntimeNodeFactory> Factories { get; }
```

### `RuntimeNodeFactory`
```
delegate RuntimeNode RuntimeNodeFactory(RuntimeNodeFactoryContext context)
```

### `RuntimeNodeFactoryContext`
```
sealed record RuntimeNodeFactoryContext(
  NodeName     Name,
  NodeDefinition Definition,
  string?      WorkflowName,
  IReadOnlyDictionary<NodeName,RuntimeNode> Resources)
  bool       IsResource  { get; }
  NodeAddress Address    { get; }
  RuntimeNode GetResource(NodeName resourceName)
```

### `RuntimeNode`
```
sealed record RuntimeNode(
  NodeAddress             Address,
  IFlowNode               Node,
  IReadOnlyList<InputPort>  Inputs,
  IReadOnlyList<OutputPort> Outputs,
  int                     Phase = 0)
  static RuntimeNode Create(NodeAddress, IFlowNode, inputs?, outputs?, phase?)
  InputPort?  FindInput(PortName port)
  OutputPort? FindOutput(PortName port)
```

### `InputPort` (abstract)
```
abstract class InputPort
  PortAddress Address    { get; }
  Type        ValueType  { get; }
  abstract Task Completion { get; }
  abstract void Complete()
  abstract void Fault(Exception)
```

### `InputPort<T> : InputPort`
```
class InputPort<T>(PortAddress address, ITargetBlock<T> target) : InputPort
  ITargetBlock<T> Target   { get; }
```

### `OutputPort` (abstract)
```
abstract class OutputPort
  PortAddress Address             { get; }
  Type        ValueType           { get; }
  abstract Task Completion        { get; }
  abstract bool DrainWhenUnlinked { get; }
  abstract IDisposable? TryLinkTo(InputPort input, bool propagateCompletion, out ApplicationRuntimeBuildError? error)
  abstract IDisposable  LinkToDiscard()
```

### `OutputPort<T> : OutputPort`
```
class OutputPort<T>(PortAddress address, ISourceBlock<T> source, bool drainWhenUnlinked = true)
  ISourceBlock<T> Source { get; }
```

### `ApplicationRuntimeBuilder`
```
class ApplicationRuntimeBuilder
  ApplicationRuntimeBuilder(RuntimeNodeFactoryRegistry factories, ApplicationDefinitionValidator? validator = null)
  ApplicationRuntimeBuildResult Build(ApplicationDefinition definition)
```

### `ApplicationRuntimeBuildResult`
```
sealed record ApplicationRuntimeBuildResult
  bool IsSuccess { get; }
  ApplicationRuntime? Runtime { get; }
  IReadOnlyList<ApplicationRuntimeBuildError> Errors { get; }
  static ApplicationRuntimeBuildResult Succeeded(ApplicationRuntime, ApplicationDefinitionValidationResult)
  static ApplicationRuntimeBuildResult Failed(ApplicationDefinitionValidationResult, ...)
```

### `ApplicationRuntimeBuildError`
```
sealed record ApplicationRuntimeBuildError(
  ApplicationRuntimeBuildErrorCode Code,
  string   Message,
  string?  WorkflowName = null,
  NodeName? NodeName    = null,
  PortName? PortName    = null)
```

### `ApplicationRuntimeBuildErrorCode`
```
enum ApplicationRuntimeBuildErrorCode
  ValidationFailed, UnknownNodeType, FactoryFailed,
  MissingInputPort, MissingOutputPort, PortTypeMismatch, LinkFailed
```

### `ApplicationRuntime : IAsyncDisposable, IDisposable`
```
sealed class ApplicationRuntime
  IReadOnlyList<RuntimeNode>           Resources    { get; }
  IReadOnlyList<Workflow>              Workflows    { get; }
  IEnumerable<RuntimeNode>             Nodes        { get; }
  ApplicationState                     State        { get; }
  ISourceBlock<ApplicationStateChanged> StateChanges { get; }
  ISourceBlock<FlowEvent>              Events       { get; }
  Task                                 Completion   { get; }
  Task  StartAsync(CancellationToken ct = default)
  void  Complete()
  void  Fault(Exception exception)
```

### `ApplicationState`
```
enum ApplicationState { Idle, Starting, Running, Stopping, Stopped, Faulted }
```

### `ApplicationStateChanged`
```
sealed record ApplicationStateChanged(ApplicationState Previous, ApplicationState Current, Exception? Exception)
```

### `Workflow : IAsyncDisposable, IDisposable`
```
sealed class Workflow
  WorkflowName                         Name         { get; }
  IReadOnlyList<RuntimeNode>           Nodes        { get; }
  WorkflowState                        State        { get; }
  ISourceBlock<WorkflowStateChanged>   StateChanges { get; }
  Task                                 Completion   { get; }
  Task  StartAsync(CancellationToken ct = default)
  void  Complete()
  void  Fault(Exception exception)
```

### `WorkflowState`
```
enum WorkflowState { Idle, Starting, Running, Stopping, Stopped, Faulted }
```

### `WorkflowStateChanged`
```
sealed record WorkflowStateChanged(WorkflowName Name, WorkflowState Previous, WorkflowState Current, Exception? Exception)
```

### `ApplicationRuntimeNodeStartException`
```
sealed class ApplicationRuntimeNodeStartException(NodeAddress nodeAddress, Exception innerException)
  : Exception
  NodeAddress NodeAddress { get; }
```

---

## FluxFlow.Engine.Mapping

### `IFlowMapper<TInput, TOutput>`
```
interface IFlowMapper<in TInput, out TOutput>
  TOutput Map(TInput input, FlowMapContext context)
```

### `IFlowPredicate<TInput>`
```
interface IFlowPredicate<in TInput>
  bool IsMatch(TInput input)
```

### `IFlowExpressionEngine`
```
interface IFlowExpressionEngine
  string  Name { get; }
  object? Evaluate(string expression, FlowMapContext context, Type resultType)
  T       Evaluate<T>(string expression, FlowMapContext context)   // default impl
```

### `IFlowMapContextFactory<TInput>`
```
interface IFlowMapContextFactory<in TInput>
  FlowMapContext Create(TInput input)
```

### `FlowMapContext`
```
sealed record FlowMapContext
  IReadOnlyDictionary<string,object?> Variables = {}
```

### `DelegateFlowMapper<TInput, TOutput>`
```
sealed class DelegateFlowMapper<TInput, TOutput>(Func<TInput, FlowMapContext, TOutput> map)
  : IFlowMapper<TInput, TOutput>
```

### `DelegateFlowPredicate<TInput>`
```
sealed class DelegateFlowPredicate<TInput>(Func<TInput, bool> predicate)
  : IFlowPredicate<TInput>
```

### `DynamicExpressoFlowExpressionEngine`
```
sealed class DynamicExpressoFlowExpressionEngine : IFlowExpressionEngine
  string Name => "dynamic-expresso"
  object? Evaluate(string expression, FlowMapContext context, Type resultType)
```

### `JsonataFlowExpressionEngine`
```
sealed class JsonataFlowExpressionEngine : IFlowExpressionEngine
  string Name => "jsonata"
  object? Evaluate(string expression, FlowMapContext context, Type resultType)
```

---

## FluxFlow.Engine.Scenarios

### `ScenarioRunner`
```
sealed class ScenarioRunner(ScenarioStepRunnerRegistry registry)
  Task<ScenarioRunResult> RunAsync(string name, ScenarioDefinition scenario, ISourceBlock<FlowEvent> events, CancellationToken ct = default)
  Task<ScenarioRunResult> RunAsync(string name, ScenarioDefinition scenario, ISourceBlock<FlowEvent> events, ScenarioStepServices services, CancellationToken ct = default)
```

### `ScenarioStepRunnerRegistry`
```
sealed class ScenarioStepRunnerRegistry
  ScenarioStepRunnerRegistry Register(IScenarioStepRunner runner)
  bool TryGet(string type, out IScenarioStepRunner runner)
```

### `IScenarioStepRunner`
```
interface IScenarioStepRunner
  string Type { get; }
  Task<ScenarioStepResult> RunAsync(ScenarioStepRunContext context, CancellationToken ct = default)
```

### `ExpectEventScenarioStepRunner`
```
sealed class ExpectEventScenarioStepRunner : IScenarioStepRunner
  string Type => "expect.event"
```

### `ScenarioStepRunContext`
```
sealed record ScenarioStepRunContext
  string                   ScenarioName
  string                   StepName
  ScenarioStepDefinition   Step
  ScenarioEventJournal     Events
  ScenarioRunLifetime      Lifetime
  ScenarioStepServices     Services
  int                      EventOffset
  IReadOnlySet<int>        ConsumedEventIndexes
```

### `ScenarioStepServices`
```
sealed class ScenarioStepServices
  static ScenarioStepServices Empty { get; }
  ScenarioStepServices Add<TService>(TService service)    // returns new instance
  bool TryGet<TService>(out TService service)
  TService GetRequired<TService>()
```

### `ScenarioRunResult`
```
sealed record ScenarioRunResult
  string                           Name
  ScenarioRunStatus                Status
  DateTimeOffset                   StartedAt
  DateTimeOffset                   FinishedAt
  IReadOnlyList<ScenarioStepResult> Steps
```

### `ScenarioRunStatus`
```
enum ScenarioRunStatus { Passed, Failed, Canceled }
```

### `ScenarioStepResult`
```
sealed record ScenarioStepResult
  string                Name
  string                Type
  ScenarioStepRunStatus Status
  DateTimeOffset        StartedAt
  DateTimeOffset        FinishedAt
  string?               Message             = null
  int?                  MatchedEventIndex   = null
  int                   NextEventOffset
  bool IsSuccess => Status == Passed
```

### `ScenarioStepRunStatus`
```
enum ScenarioStepRunStatus { Passed, Failed, Canceled, Skipped }
```

### `ScenarioStepTypes`
```
static class ScenarioStepTypes
  const string MqttPublisher = "mqtt.publish"
  const string MqttTrigger   = "mqtt.subscribe"
  const string ExpectEvent   = "expect.event"
  IReadOnlySet<string> All { get; }
```

### `ScenarioStepConfigurationKeys`
```
static class ScenarioStepConfigurationKeys
  const string Connection      = "connection"
  const string Topic           = "topic"
  const string Payload         = "payload"
  const string Qos             = "qos"
  const string Retain          = "retain"
  const string Subscriptions   = "subscriptions"
  const string TimeoutSeconds  = "timeoutSeconds"
  const string Type            = "type"
  const string Status          = "status"
  const string Subject         = "subject"
  // ... and more
```

### `ScenarioEventJournal`
```
sealed class ScenarioEventJournal : IDisposable
  // Constructed by ScenarioRunner; step runners access it via context
  IReadOnlyList<FlowEvent> Events { get; }
```

### `FlowEventExpectation`
```
sealed class FlowEventExpectation
  // Created internally by ExpectEventScenarioStepRunner
```

---

## FluxFlow.Engine (root namespace)

### `FlowApplicationHost : IAsyncDisposable, IDisposable`
```
sealed class FlowApplicationHost
  // Constructors
  FlowApplicationHost(IConfiguration?, ApplicationRuntimeBuilder, FlowApplicationConfigurationLoader?,
                      string sectionName, ScenarioRunner?, ApplicationDefinition?)

  // Static factories (preferred)
  static FlowApplicationHost Create(IConfiguration configuration, RuntimeNodeFactoryRegistry registry)
  static FlowApplicationHost Create(ApplicationDefinition definition, RuntimeNodeFactoryRegistry registry)

  // Scenario helpers
  static ScenarioRunner                CreateDefaultScenarioRunner()
  static ScenarioStepRunnerRegistry    CreateDefaultScenarioStepRunnerRegistry()

  // State
  FlowApplicationHostState        State           { get; }
  ApplicationDefinition?          Definition      { get; }
  ApplicationRuntime?             Runtime         { get; }
  FlowApplicationHostBuildResult? LastBuildResult { get; }
  Exception?                      LastException   { get; }

  // Lifecycle
  FlowApplicationHostBuildResult          Build()
  FlowApplicationHostBuildResult          Start()
  Task<FlowApplicationHostBuildResult>    StartAsync(CancellationToken ct = default)
  Task<FlowApplicationHostBuildResult>    StartBuiltAsync(CancellationToken ct = default)
  Task<ScenarioRunResult>                 RunScenarioAsync(string scenarioName,
                                              Func<ApplicationRuntime,ScenarioStepServices>? servicesFactory = null,
                                              CancellationToken ct = default)
  Task                                    StopAsync(CancellationToken ct = default)
```

### `FlowApplicationHostState`
```
enum FlowApplicationHostState { Empty, Built, Running, Stopped, Faulted }
```

### `FlowApplicationHostBuildResult`
```
sealed record FlowApplicationHostBuildResult
  bool IsSuccess { get; }
  IReadOnlyList<FlowApplicationHostBuildError> Errors { get; }
```

### `FlowApplicationHostBuildError`
```
sealed record FlowApplicationHostBuildError(
  FlowApplicationHostBuildErrorCode Code,
  string    Message,
  Exception? Exception   = null,
  string?    WorkflowName = null,
  string?    NodeName     = null)
```

### `FlowApplicationHostBuildErrorCode`
```
enum FlowApplicationHostBuildErrorCode
  InvalidConfiguration, ValidationFailed, BuildFailed, StartFailed
```

### `FlowApplicationConfigurationLoader`
```
sealed class FlowApplicationConfigurationLoader
  const string DefaultSectionName = "FluxMq:FlowApplication"
  ApplicationDefinition Load(IConfiguration configuration, string sectionName = DefaultSectionName)
```

### `FlowApplicationConfigurationException`
```
sealed class FlowApplicationConfigurationException(string message, Exception? inner = null)
  : Exception
```
