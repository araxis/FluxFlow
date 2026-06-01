# Package README Composition Links

Date: 2026-06-01

Added a short `Composition Guidance` section to each component package README
under `src/FluxFlow.Components.*`.

Decision:

Keep package README files focused on package-specific contracts, options, and
registration. Link to `docs/12-component-composition.md` for the shared
host/package boundary model instead of duplicating that guidance in every
package.

Touched packages:

- Control
- FileSystem
- Http
- Mapping
- Metrics
- Mqtt
- Observability
- Payloads
- Serialization
- Sessions
- State
- Timers
- Validation
