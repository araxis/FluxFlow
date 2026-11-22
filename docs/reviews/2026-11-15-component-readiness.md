# Existing component readiness review

This pass reviewed the 37 installed catalog entries across all 12 plugin families. It adds no component types or execution features. Runtime behavior, saved configuration formats and plugin contracts are unchanged.

## Editor coverage

Every catalog entry was rendered in the shared Inspector and opened in the published Studio browser with freshly packaged plugins. The automated catalog sweep checks that rendering leaves saved configuration untouched, unknown fields survive, delete/Undo/Redo restores the project, and saving while plugins are absent preserves the project for reopening after reinstall.

| Family | Components reviewed |
| --- | --- |
| Bluetooth | Client, Notifications, GATT Server |
| Serial | Text, Reader, Writer |
| MQTT | Publisher, Subscriber, Connection Controller |
| RabbitMQ | Publisher, Consumer, Acknowledge |
| NATS | Publisher, Subscriber, Request, Reply, JetStream Publisher, Consumer, Settlement |
| HTTP | Client, Trigger |
| ROS 2 | Publisher, Subscriber |
| Routing | Gate, Router, Change Filter, Deduplicate |
| Flow Control | Rate Limit, Request–Reply, Retry |
| Sources | Scheduler, Source, Data Generator |
| Transformation | JSONata Mapper, Reducer, Batch |
| Logging | Logger |

Existing component-specific tests cover validation, conditional properties and ports, collections, nested editors, stale drafts, Cancel, exact text preservation and saved wiring. Arbitrary message bodies, expressions and custom JSON remain data editors; their structure is not inferred. Advanced JSON remains available alongside visual settings.

## Corrections

- Plugins → Settings now has a clear default source, named source cards, an empty state, responsive fields and visible unsaved state. It reuses the existing page chrome, controls, source store and validation.
- Back asks before discarding unsaved source changes. Save and Discard remain unavailable when unchanged or saving.
- After a successful source write followed by a failed reload, editing is disabled until Retry reloads the authoritative sources. This prevents resubmitting an obsolete concurrency baseline.
- The Settings footer correctly identifies companion-owned sources.
- The smallest viewport puts the default-feed badge below its URL rather than squeezing the URL into a narrow column.
- Portable expression properties use the existing full-width expression row. Change Filter, Deduplicate and Data Generator no longer repeat the label in a separate column.

## Verification

- Complete Studio suite: 1,980 passed on each of .NET 8, 10 and 11 (5,940 total).
- Focused final editor, expression and Settings regressions: 106 passed on each framework (318 total).
- Component and coordination/runtime unit suites: 1,142 passed across their configured .NET 8/10 targets.
- Environment integration without Docker: 173 passed on each of .NET 8, 10 and 11 (519 total).
- RabbitMQ local-container integration: 29 passed on each of .NET 8 and 10 (58 total).
- NATS/JetStream local-container integration: 48 passed on each of .NET 8 and 10 (96 total).
- Published .NET 11 browser: all 37 component inspectors opened using rebuilt packages. Source editing, duplicate rejection, Back/Cancel, Discard and persistence after reload were checked in an isolated store. Responsive checks used 640px and 360px widths.

The browser trial used a temporary project and plugin store. User projects, source preferences and tabs were not changed. Build output still contains existing analyzer warnings; a passing test run does not mean a warning-free build.

## Limits retained explicitly

- BLE and Serial controlled tests do not certify a particular adapter or physical device. No servo movement or hardware writes were performed in this pass. The existing unconfirmed GATT Stop limitation remains unconfirmed.
- ROS 2 needs native ROS libraries and generated message type registration on the execution host. Its controlled tests do not establish native deployment readiness.
- At the time of the initial review, Rate Limit required a host-provided `RateLimiter` resource and was not runnable in the standard companion. The follow-up below resolves this limitation.
- The browser sweep verifies rendering and configuration preservation, not every possible value or hardware topology. Existing tests provide behavioral coverage; these limits are not hidden behind a claim of universal production readiness.

Core and WFENG were not modified.

## Settings and expression-row follow-up review

The main menu previously bypassed the Settings page's Back confirmation and could silently discard a source draft. Page-changing menu actions now use the same confirmation. Cancel leaves the draft in place; Discard restores the saved sources before navigating. Navigation is blocked during a save or an outstanding confirmation. No new navigation service or persistence format was introduced.

Regression coverage includes menu navigation to Plugins, Registries and Resource defaults, rejected writes, pending writes, Discard during a pending save, and a successful write followed by a failed reload. The complete Studio suite passes 1,988 checks on each of .NET 8, 10 and 11 (5,964 total). The shared expression-row implementation needed no further changes. Settings layout checks at 320px and 390px found no clipping that prevented editing.

## Rate Limit completion

Flow Control 1.0.0-alpha.2 supplies `rateLimiter.tokenBucket` through the existing resource registration and portable design contracts. The shared connection Property Grid exposes burst size, refill count, refill interval and queue limit. Rate Limit selects compatible resources through the shared selector; existing unresolved references remain saved until explicitly changed. No component runtime, Core or WFENG implementation changed.

The companion recognizes this in-process resource and rejects missing limiter configuration before starting resources. Application and workflow ownership use the existing DI scopes. Native token-bucket disposal releases waiting acquisitions and stops replenishment. Custom hosts can still supply their own native limiters through the existing registrar.

Verification on .NET 8, 10 and 11:

- Complete Studio suite: 1,991 passed per framework (5,973 total), including resource edits, invalid and unchanged values, missing references, missing-plugin save/reopen and Undo/Redo.
- Companion, execution-log/trace and resource integration selection: 67 passed per framework (201 total), including queue rejection, cancellation, owner disposal, queued Stop draining, reply timeouts, retained observations and fresh reruns.
- Rate Limit and Request–Reply runtime suite: 21 passed per framework (63 total), including late replies and correlation.
- Packaged plugin consumer passed both before removal and after reinstall on all three runtimes, using both the new resource and existing host-supplied limiters at application and workflow scope.
- Browser check against the installed package: Add connection, flat Property Grid, finite timeout, compatible resource selection, Advanced JSON Cancel and value Undo/Redo. The test used an isolated store and temporary project; existing tabs and projects were preserved.

Graceful Stop remains unchanged: it drains accepted work. An infinite response timeout can keep Stop waiting when a reply never arrives. The limiter controls dispatch rate, not concurrency; replies do not return consumed tokens. Existing analyzer warnings remain.

## Rate Limit follow-up review — 2026-11-16

Confirmed a scope mismatch: a workflow could select a token bucket owned by another workflow, and the companion check accepted that reference even though native resource resolution rejected it at startup. The plugin now rejects that selection during Check. Its portable resource metadata marks workflow resources as local, allowing the shared selector to offer application resources and the current workflow's resources while preserving an invalid saved reference for correction.

The scope metadata is opt-in. Older catalogs and other plugins retain their existing rules, including BLE's cross-workflow references. Regression coverage checks metadata transport with and without the new member, valid application/local references, invalid foreign references, exact saved values, save/reopen and Undo/Redo. Run tests also check that correlation IDs are distinct across reruns.

No Rate Limit runtime, configuration format, queue policy or Stop behavior changed. No Core or WFENG files changed.

Final verification on .NET 8, 10 and 11: 1,994 Studio tests per framework (5,982 total), 78 companion/resource/logging/plugin-loading integration tests per framework (234 total), and 21 Rate Limit/Request–Reply runtime tests per framework (63 total). All passed. Existing Studio analyzer warnings remain.
