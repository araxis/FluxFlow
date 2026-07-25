# Canonical vNext Cleanup Completion

Date: 2026-07-25

## Status

The canonical vNext cleanup is complete on local branch
`work/canonical-vnext-cleanup`. The repository now maintains one application
model, one executable runtime path, and one public component implementation per
operation. No branch push, tag, package publication, pull request, or merge was
performed.

## Requirement Audit

| Program area | Completion evidence |
| --- | --- |
| Audit and removal ledger | `eng/canonical-vnext-cleanup-ledger.json` contains 27 reviewed entries: 23 removed after parity, one removed after migration, one completed internal consolidation, and two retained after explicit review. Its eight invariants describe the canonical model. Release tests validate the ledger and package/API conventions. |
| Canonical configuration | `ApplicationDefinition` persists exactly `Resources` and `Workflows`; keys supply identity; component properties are flat; canonical JSON/configuration codecs, aliases, shorthand, defaults, addresses, conditions, and round trips are covered by the 109 Composition tests. Legacy shapes enter only through explicit one-way migrators. |
| Composition | The executable legacy definition/runtime path and node-oriented factory compatibility are removed. Canonical normalization, link compilation, factory contexts, fan-in completion, ownership, and aggregate cleanup are the maintained path. Composition and Hosting passed 109 and 29 tests. |
| Engine | The duplicate Engine model/runtime is removed behind an explicit migrator. Runtime preparation, resource/component activation, stable ports, workflow/revision binding, rollback, cleanup, and generation ownership are focused collaborators. Engine passed 55 tests. |
| Structural components | Control Filter/When and Routing Switch/Fork/Merge are removed after conditional-link, default-route, fan-in, fan-out, and cross-workflow parity tests. |
| Component families | Payloads, Metrics, Projections, HTTP, FileSystem, Storage, Mapping, Validation, Assertions, Expectations, State, Sessions, Timers, Sources, Observability, Routing, and MQTT each expose one canonical component path. Numbered notes 243 through 260 contain family-specific behavior, version, API, package, and consumer evidence. |
| MQTT | Broker/client/controller boundaries, command results, trigger messages, subscription forms and ownership, reconnect, Ack/Nak correlation, diagnostics, and adapter ownership are consolidated behind one controller facade and explicit Composition modules. MQTT passed 48 tests. |
| Reliability | Direct regressions cover revision activation/replacement/rollback, stable generations, startup cancellation, fan-in completion and first fault, bounded ordered diagnostics, aggregate cleanup, expected failures as data, timer/completion races, confined FileSystem paths and bounded reads, HTTP charset decoding, and MQTT reconciliation and acknowledgement. |
| Documentation and migration | Runtime architecture, data contracts, component names, canonical migration, and Engine migration are current in docs 19 through 23. `memory/01-current-state.md` is reduced to current facts and decisions. |

## Removal And Retention Results

- No obsolete declaration remains under `src/`.
- No maintained source uses reflection or assembly scanning for registration.
- No retired structural node, public generic component node, or duplicate
  legacy runtime type remains in maintained source.
- Normal component failures use normal result Output values. Remaining Errors
  streams belong to runtime diagnostics or explicit support/adapter boundaries,
  not canonical component business-result routing.
- The 13 audited files above 500 lines remain because each is the sole
  behavior-rich implementation for an ownership-sensitive responsibility.
  They are not parallel component or runtime paths.
- Explicit `System.Threading.Tasks.Dataflow` references remain for net8.0 in
  multi-target packages. `NU1510` is classified narrowly because net10.0 can
  prune the package while the net8.0 artifact still requires it.

## Versions And Compatibility

- Public removals use the reviewed next-major package versions recorded by the
  owning family notes and `eng/packages.json`.
- Unaffected packages and adapters were not bumped solely for cleanup.
- Public API baselines were regenerated only for reviewed declaration changes.
- SDK package validation reports only documented intentional removals against
  preceding published versions; no compatibility suppression was added.
- Release preflight and complete local-source dry-runs passed for affected
  foundation and component packages during their bounded commits.

## Final Verification

- Data: 32 passed.
- Nodes: 41 passed.
- Composition: 109 passed.
- Composition Hosting: 29 passed.
- Fluent: 21 passed.
- Engine: 55 passed.
- Designer: 112 passed.
- Configuration: 40 passed.
- FileSystem: 43 passed.
- HTTP: 22 passed.
- Timers: 72 passed.
- Routing: 51 passed.
- Routing Composition: 13 passed.
- MQTT: 48 passed.
- Release: 99 passed.
- Controlled Debug and Release solution confirmation builds each completed 129
  projects with zero errors and zero warnings after the final source changes.
- All 58 manifest packages were packed into a fresh temporary source outside
  the repository.
- A fresh net8.0 consumer with 58 direct package references restored against
  the complete package source and built in Release with zero warnings and zero
  errors.
- Graphify rebuilt the ignored local graph to 15,980 nodes, 24,558 edges, and
  1,610 communities; `graphify-out/` remains excluded from git.

## Deferred Work

Supervision, polling/latest-value APIs, durable mailboxes, broker clusters,
automatic mapper insertion, custom containers, cyclic graph execution, and
hot-reload enhancements remain separate future designs. They were not used to
redefine or narrow this cleanup.

## Outcome

The canonical architecture is the only maintained implementation, intentional
removals and migration paths are documented, the remaining reviewed exceptions
have explicit ownership and warning rationale, and no cleanup blocker remains.
