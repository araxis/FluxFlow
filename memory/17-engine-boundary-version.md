# Engine Boundary Version

Date: 2026-05-31

## Decision

Version `0.2.0-alpha.1` is the engine-only boundary release.

`FluxFlow.Engine` now owns executable workflow structure and runtime behavior:

- `ApplicationDefinition.Resources`
- `ApplicationDefinition.Workflows`
- runtime build/start/stop/dispose lifecycle
- runtime events
- runtime diagnostics
- mapping contracts and expression engines
- node authoring helpers

It no longer owns scenario or test structure:

- no `ApplicationDefinition.Tests`
- no scenario/test validation in `ApplicationDefinitionValidator`
- no scenario runner APIs on `FlowApplicationHost`
- no scenario step registry or built-in step runners

## Application Guidance

Applications can keep any workspace shape they want. A consuming app should load
its own workspace document, then project only executable resources and workflows
into `FluxFlow.Engine.ApplicationDefinition`.

FluxMq should keep dashboards, tests, MQTT scenario steps, and scenario reports
in FluxMq. Its migration should start with a `FluxMqApplicationDefinition` and a
projection method into `ApplicationDefinition`.
