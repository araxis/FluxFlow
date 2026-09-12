# Messaging design boundary

Status: implementation in progress; part of the approved full core refactor.

| Package | Disposition | Targets and dependencies |
|---|---|---|
| FluxFlow.Components.Mqtt | Retain all runtime components, clients and protocols unchanged | net8.0/net10.0; existing references |
| FluxFlow.Components.Mqtt.Composition | Retain trusted portable bindings, options and resource lifetimes; remove presentation dependency | net8.0/net10.0; runtime module and Composition |
| FluxFlow.Components.Mqtt.Designer | New optional projection of the four registered descriptors; move event descriptions here | net8.0/net10.0; Mqtt.Composition and Components.Designer |
| FluxFlow.Components.Designer | Reuse AddDesignMetadata without new public infrastructure | net8.0/net10.0; unchanged references |

Register runtime contracts with AddMqtt. Consumers needing visual hints explicitly
call AddMqttDesign afterwards. Direct constructors and instance-first graph usage
remain independent of either registration path. Structural port kinds, shapes,
cardinality and required resources/options come from runtime descriptors; the
optional projection only supplies presentation. It never creates components or
resolves a client. Source/runtime algorithms and broker protocols are not rewritten.

Migrate the existing module unit-test setup, family release metadata fixtures and
Designer sample to explicit presentation opt-in. Retain the full runtime tests:
resource ownership, nested portable configuration, host bindings, revision cleanup,
and constructor-based use. Add tests for independence, descriptor identity, exact
event fields/roles, enum defaults/choices, and idempotent optional registration.

Update solution, package manifest, reviewed public API, package README/release notes,
overview and changelog together. Register alias/tag prefix components-mqtt-designer;
start the optional package at 1.0.0-rc.1 without changing existing unreleased versions.
Pack locally and inspect both target dependency groups. This is not a complete
candidate feed, external consumer acceptance, or permission to publish.

After this migration, 17 other ordinary module registration dependencies remain.
The full architecture, performance and isolated package-consumer gates remain open.
