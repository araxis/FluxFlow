# Findings

Date: 2026-05-31

## Source issues

1. Transport-specific scenario code leaked into the engine.
   - `ScenarioStepTypes` included `mqtt.publisher` and `mqtt.trigger`.
   - `ScenarioStepConfigurationKeys` included connection, QoS, retain, subscription, and payload encoding keys.
   - `ScenarioStepDefinitionValidator` validated transport-specific publisher and trigger steps.
   - `ExpectEventScenarioStepRunner` had transport-specific timeout guidance.

2. Event type constants leaked component concerns.
   - `FlowEventTypes` listed transport, file, schema, and assertion event names that belong to component packages or consuming applications.

3. Configuration still referenced the source application.
   - `FlowApplicationConfigurationLoader.DefaultSectionName` used `FluxMq:FlowApplication`.

4. UI layout definitions live in the engine.
   - `DashboardDefinition` and its validation are not required for a bare workflow runtime.
   - This should move to a future UI or designer package unless the engine must own persisted design metadata.

5. Package readiness was incomplete.
   - The library lacked repository metadata, package readme metadata, symbol package setup, and a release workflow.
   - There was no local git repository or private remote.

6. Tests are too thin for a reusable package.
   - Current tests cover only a few builder smoke paths.
   - Missing coverage includes typed port mismatch, completion propagation, multiple input completion, event collection, JSON round-trip, scenario timeout behavior, and host lifecycle.

7. Documentation still contains source-application examples.
   - Docs and README mention transport examples, source application names, and component concepts.
   - The docs need a cleanup pass after source boundaries settle.
