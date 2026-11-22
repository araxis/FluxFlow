# Configuration editor harmonization

Baseline: 2026-11-06. Sources are `StudioServiceCollectionExtensions`, registered
`IComponentDescription` inspectors, configuration models and existing editors.
There are 37 registered workflow component types, nine connection descriptions
and five environment service providers. Source inspection is not browser acceptance.

Status: the editor audit covers all 37 registered workflow components, nine
connection descriptions and five service providers. The tables summarize
acceptance and exceptions; the dated work notes below record intermediate
findings. These checks cover configuration editing, not deployment or execution.

## Shared foundation

- [x] Inventory registrations and distinguish inline, dialog and JSON-only paths.
- [x] Document shared grid, dialog and toolbar conventions.
- [x] Reject stale shared field-dialog results after graph revision, disposal or
  replacement of the edited configuration.
- [x] Preserve stored JSON and redo for formatting-only shared field-dialog saves.
- [x] Apply shared property-row semantics to structured settings dialogs.
- [x] Keep invalid visual field drafts open on Save and Visual-to-JSON switching.
- [x] Expose one optional whole-component JSON action for every registered component,
  retaining specialized editors and sharing the fallback for the remaining types.
- [x] Verify the shared foundation and first inline-editor batch on .NET 8 and .NET 10 and in the browser.

## Workflow components

| Type | Current visual surface | Remaining review or gap |
| --- | --- | --- |
| scheduler | Shared validated duration, date and cron rows; adjacent seconds option | Date/offset preservation, positive interval, native cron syntax, seconds-mode validation, history and save/reopen verified; timezone identifiers are retained for the runner, whose timezone database must be checked at execution |
| source | Shared visual value list and JSON/file source editor | Typed Add/Edit/Remove, exact numeric values, duplicate object members, mode switching, invalid drafts, Cancel, stale results, history and save/reopen verified; external file contents remain runner-owned |
| mapper | Shared expression editor and required Engine row for imported engines | Imported engine edit, retained file expression and unknown settings verified through save/reopen; the JSONata palette preset is unchanged |
| batch | Shared flat integer and bounded duration fields | Configuration-editor acceptance verified: all three settings, required values, native bounds, exact duration text, history, narrow layout and save/reopen |
| gate | Shared flat checkbox and bounded integer capacity | Configuration-editor acceptance verified: both settings, omitted initial-state default, history, narrow layout and save/reopen |
| changeFilter | Shared required Engine, string-result expression editor, comparison and bounded capacity | Configuration-editor acceptance verified: custom engine names, exact expression preservation, Save/Cancel, comparison, capacity, history, narrow layout and save/reopen; engine availability remains runner-owned |
| deduplicate | Shared required Engine, string-result expression editor, comparison, positive retention and bounded capacities | Configuration-editor acceptance verified: required positive retention without a timer cap, custom engines, expression Save/Cancel, comparison, capacities, history, narrow layout and save/reopen |
| dataGenerator | Shared source expression editor and validated Locale | Source editing and locale repair/history/save-reopen verified; generator evaluation remains runner-only |
| rateLimit | Shared required text, bounded duration and integer fields | Configuration-editor acceptance verified: manual host key, required validation, exact timeout, capacity, history, narrow layout and save/reopen; generic companion has no limiter factory |
| requestReply | Shared bounded integer and duration fields; dependent capacity bounds | Configuration-editor acceptance verified: all four settings, combined native capacity limit, default restoration, timeout, keyboard selection, history, narrow layout and save/reopen |
| reducer | Shared expression and JSON/file source editors | File-backed state no longer bypasses registration validation. Invalid capacity, Cancel and retained file reference verified; external file contents and state-aware evaluation remain runner-only |
| retry | Inline policy settings and Boolean expression preview with value/error samples | Native bounds and both reply types verified. Sample switching retains separate drafts; Cancel leaves history untouched. Expression Save/Undo/Redo and save/reopen verified; preview evaluates only the predicate |
| router | Inline route collection; optional JSON | Add/Edit/Remove, Cancel, stale results, retained output links and history covered by rendered tests. Edited predicate and unchanged port binding verified in the saved browser project |
| http.client | Shared typed connection selector and bounded integer capacity | Configuration-editor acceptance verified: compatible choices, retained missing references, keyboard selection, Undo/Redo, narrow layout and save/reopen |
| logger | Metadata rows with optional typed resource selection, validated category and bounded capacity; shared component JSON | Visual acceptance verified; whole-component JSON invalid drafts, exact values, cancellation, selection and history verified through rendered tests |
| mqtt.connection | Shared typed connection and bounded capacity rows; optional component JSON | Missing reference repair, invalid capacity, JSON drafts, Cancel, stale edits, Undo/Redo, narrow width and save/reopen verified |
| mqtt.publisher | Shared metadata rows with typed connection, bounded capacity and component JSON | Native property coverage, publish-request documentation, filtered/missing choices and whole-component JSON validation, preservation, history and save/reopen verified; readable browser-native validation verified |
| mqtt.subscriber | Shared metadata rows, inline Topics group, field JSON and shared component JSON | Native topic/Manual QoS validation, linked diagnostics, invalid drafts, Cancel, repair, mode switching, history, save/reopen and whole-component draft preservation verified |
| amqp.publisher | Shared rows, validated inline declarations and typed argument collections | Configuration-editor acceptance verified: numeric bounds, preserved defaults, UTF-8 name/routing limits, declaration count, nested arguments, optional JSON, history, narrow layout and save/reopen; publish-request contract remains unchanged |
| amqp.consumer | Shared rows with Queue/Prefetch/Requeue and shared declarations | Configuration-editor acceptance verified: Queue/native numeric bounds, Requeue and saved feedback wiring, declaration/argument transactions, optional JSON, history, narrow layout and save/reopen; broker behavior is outside this UI audit |
| amqp.settlement | Shared typed component selector and bounded capacity | Configuration-editor acceptance verified: same-workflow filtering, missing references, stale targets, numeric bounds, exact saved representation, history, narrow layout and save/reopen |
| nats.publisher | Shared typed connection and bounded integer rows; optional Advanced JSON | Configuration-editor acceptance verified: filtering, missing references, bounds, JSON validation, Save/Cancel, no-op Redo and save/reopen; publication input contract unchanged |
| nats.subscriber | Shared validated subject/queue rows; optional Advanced JSON | Configuration-editor acceptance verified: native subjects, optional group reset, linked diagnostics, numeric bounds, references, JSON modes, history and save/reopen |
| nats.request | Shared typed connection and bounded integer rows; optional Advanced JSON | Configuration-editor acceptance verified: numeric bounds, references, JSON modes, Save/Cancel, history and save/reopen; request payload remains supplied by input |
| nats.reply | Shared typed connection and bounded integer rows; optional Advanced JSON | Configuration-editor acceptance verified: numeric bounds, references, JSON modes, Save/Cancel, history and save/reopen; reply payload remains supplied by input |
| nats.jetstream.publisher | Shared validated fields, conditional provisioning and exact Int64 retention | Configuration-editor acceptance verified: names/subjects, conditions, numeric bounds, saved enum representations, typed references, optional JSON, history and save/reopen |
| nats.jetstream.consumer | Shared validated fields, conditional provisioning and exact Int64 retention | Configuration-editor acceptance verified: native names/filter, hidden values, numeric bounds, saved enum representations, references, optional JSON, history and save/reopen |
| nats.jetstream.settlement | Shared typed consumer selector and bounded capacity; optional Advanced JSON | Configuration-editor acceptance verified: same-workflow filtering, missing references, stale selection, bounds, JSON validation, Save/Cancel, history and save/reopen |
| serial.text | Workflow-local port selector, LF/CRLF selector, bounded integers and optional JSON | Editor acceptance verified: native line ending and capacity validation, Cancel, missing references, history and save/reopen |
| serial.reader | Workflow-local port selector, conditional framing, delimiter presets/custom Base64 and native cross-field diagnostics | Editor acceptance verified: numeric bounds, framing, custom bytes, incompatible reference repair, history and save/reopen |
| serial.writer | Shares the reader’s frame and connection editors | Editor acceptance verified: hidden fixed length survives Raw mode, numeric bounds, narrow layout, history and save/reopen |
| ble.client | Flat target/operation rows, validated value editor, discovery picker and shared Advanced JSON | Fixed/command switching, target validation, byte preservation, history, narrow layout and save/reopen verified |
| ble.notifications | Shared target rows and discovery picker, subscription mode and shared Advanced JSON | Missing reference repair, saved numeric Indicate mode, Cancel, stale results, history and save/reopen verified |
| ble.server | Shared adapter, advertising and capacity rows; visual GATT hierarchy and synchronized JSON | Descriptor edits, sibling bytes, legacy text, invalid drafts, Cancel, Undo/Redo, narrow layout and save/reopen verified |
| ros.publisher | Typed node selector, topic, capacity, visual QoS and optional JSON | Editor acceptance verified: node repair, inherited and partial profiles, hidden depth, numeric values, invalid JSON drafts, history and save/reopen |
| ros.subscriber | Shared ROS fields, subscription queue and local delivery options | Editor acceptance verified: queue bounds, rejected Wait mode, retained saved policies, inherited QoS, Cancel, history and save/reopen |
| http.trigger | Inline Methods collection, conditional response fields and shared JSON editor | Host settings validation, timeout bounds, saved mode switching, custom methods, shutdown policy, response wiring, history and save/reopen verified; route parsing and endpoint mapping remain host-owned |

`ComponentDescriptionTests` checks metadata against configuration defaults and
registered properties. It does not prove visual completeness or runtime interaction.

## Project connections

All use the docked connection Inspector and shared connection grid. Review the
following individually, including Rename/Remove, missing references and credentials.

| Type | Existing fields/groups | Remaining review |
| --- | --- | --- |
| mqtt.client | Legacy Host/Port/TLS or Address, authentication, conditional TLS and client certificate, trusted CA entries, client and reconnect policy | Verified native address and retry validation, bound/manual fallback, secret preservation and reset on .NET 8 and 10. Published Inspector: per-file editing, Cancel, Undo/Redo, invalid JSON repair, conditional TLS, 800 px layout and save/reopen. Saved configuration differed only in the two edited values. |
| http.client | Connection, authentication, retry, visual Headers and optional JSON. Inline address and duration rules match registration. | Published Inspector: mixed-case Basic and numeric backoff display by name; conflicting saved credentials remain repairable. Invalid Advanced JSON stays open, Cancel leaves history untouched, and offline OAuth validation, Undo/Redo, 800 px layout and save/reopen passed. Comparing saved settings confirmed only the edited address and explicitly removed token changed. |
| amqp.connection | Connection, credential variable names and recovery; interval is hidden while recovery is off | Published Inspector: keyboard toggle, Undo/Redo and save/reopen retained the inactive interval (including its saved string representation), credential references and extension fields. Endpoint and Advanced JSON validation tested on .NET 8 and 10. |
| nats.connection | Connection, credential variables, TLS and reconnect; alternative authentication fields return when the current method is cleared | Published Inspector: token to credentials-file reference, conflicting Advanced JSON rejected, Cancel, save/reopen and 800 px viewport. Config comparison preserved numeric TLS, unrelated settings and environments. Tests cover incomplete credentials and repairing conflicting saved TLS references without resolving host secrets. |
| serial.port | Editable port name with companion discovery, line settings, flow control and bounded read/write durations | Published Inspector: listed real companion ports without opening them, selected a port, undid/redid the selection, entered a remote port manually, repaired an invalid timeout and reopened at 800 px. Saved configuration changed only the port and timeout. Dual-framework tests also cover cancellation, late results and native timeout bounds. |
| ros.node | Node name and namespace in the docked grid; optional connection JSON | Creation, namespace edits, required values, Undo/Redo and save/reopen verified without starting ROS |
| ble.peripheral | Address and address type; effective endpoint per application instance | Published Inspector: editing the manual address left the bound snapshot unchanged. Save/reopen retained both endpoints at 800 px. Tests distinguish absent bindings (manual fallback) from missing or explicitly unbound services in the target picker. |
| ble.adapter | Default adapter selector; retained unsupported endpoints | Published Inspector: invalid Advanced JSON stayed open; Cancel left Undo disabled. Repair, Undo/Redo and reopen retained the manual endpoint and the wrong-host warning. The two BLE edits were the only differences in the saved project. Offline checks passed on .NET 8 and 10; no radio or peripheral was opened. |
| logging.otlp | Shared Collector and Logging groups expose all seven native settings; visual masked Headers collection and optional Advanced JSON | Creation, visual fields, whole-connection and field-level JSON validation, selection, preservation, Cancel, history, narrow layout and save/reopen verified; duplicate-name headers retain native JSON fallback |

The Rate Limit resource is a host-owned key. The companion exposes no API to
enumerate these objects, so its grid accepts a manually entered key. It is not a
tenth connection type.

## Environment, hosts and services

- Topology Inspector: general, execution, placement, bindings and service Setup.
- Host definitions: reusable host/service settings; placed snapshots remain locked.
- Shared editors: ServiceSettingsEditor, ServiceSetupEditor, ContainerImageEditor,
  BrokerAccessEditor, StartupFileEditor and ServiceDocumentDialog.
- Providers: mosquitto, rabbitmq-mqtt, rabbitmq-amqp, nats and wiremock.
- NATS service trial: the canvas and Setup show the configured address for an
  existing service and distinguish a managed address assigned at startup.
  The published Inspector retained the variable draft across Visual/JSON,
  discarded it on Cancel, and restored a token-reference edit with Undo/Redo.
  Reopening at 800 px showed the same reference with no page overflow. Comparing
  the two saved projects found only the edited reference in the saved setup.
  Missing-registry feedback appeared as a toast, with no stale Inspector row
  on reopening a different setup. No registry, broker or container was contacted.
- The Setup group reads only the selected resource. Rendered tests on .NET 8
  and 10 cover NATS, serial, both BLE resource types and a redacted HTTP address
  alongside a malformed sibling. Service documents, snapshots, profile copying
  and topology endpoint tests also passed.
- Mosquitto and RabbitMQ MQTT: the published Setup group selected the newly
  added broker user without changing the workflow connection user. Verified
  removal, Undo/Redo and persisted permissions, plus editing RabbitMQ plugins
  in Visual and Advanced text. Project updates and delayed preference saves
  have separate regression cases, including a change of target during a save.
- RabbitMQ AMQP: edited the virtual host and added a virtual host definition
  in the visual form. Verified the saved file contains that definition and
  the reopened grid shows the selected virtual host.
- WireMock: visual endpoint and optional JSON editing share one dialog. A
  mapping missing its Response remained editable after Save; correction,
  Cancel and Undo/Redo were exercised. Reopened the mapping with its response
  data and priority intact. At 600 px there was no page overflow.
- The four-provider browser trial inspected locked snapshots separately:
  broker user actions were disabled, the AMQP virtual host was read-only,
  and the plugin and WireMock source viewers had no Save action. Comparing
  the complete unlocked project before and after save/reopen found exactly
  the six intended service edits. These were offline editor checks.
- Host definition coverage includes selection and autosave, independent snapshot
  placement, moving and resizing, bindings, Undo and save/reopen
  (InfrastructureCatalogTests and SharedStudioHostingTests.Infrastructure).

## Cross-surface acceptance and handoff

Studio and project Canvas settings use the same two-column grid and color
picker. The project dialog shows only its four overrides. Studio categories
expose the current selection to keyboard and assistive technology. In the
published app, Enter selected Canvas and opened its Input color picker. At a
400 px viewport the category bar occupied 64 px without horizontal scrolling.
A failed
load of device preferences prevents a later edit from overwriting the unread
file; a successful reload restores saving. Project history is independent of
device preferences.

The complete Studio suite passed on .NET 8 and .NET 10 (1,749 tests per target).
Registration tests check type identities, including the list exported by the
headless provider. Navigation coverage includes overflow, cancellation, saved Run
selection and separate histories for project edits and host definitions.

Keep the next behavior review separate from editor acceptance. Useful first
scenarios are resolving a host-provided limiter before Run, discovering a real
BLE characteristic and cancelling on disconnect, and starting a container-backed
service with the endpoint shown by its binding. They require the respective
host, peripheral or container; editing and reopening configuration does not
prove those runtime outcomes.

## Execution order and evidence

1. Shared editing safety and grid standards.
2. NATS and Serial frame Inspectors; reuse their existing metadata and validation.
3. Routes, HTTP methods, ROS QoS and other commonly used structured values.
4. BLE GATT and remaining component families, one at a time.
5. Connections and environment/host/service surfaces.
6. Cross-surface keyboard, narrow-width, save/reopen and cleanup audit.

For each batch record the changed surfaces, tests on both target frameworks,
browser checks and unresolved limitations here. Keep the broader goal open until
all inventory rows have concrete acceptance evidence.

### First verified batch

- Shared field dialogs reject changed/disposed targets and preserve redo on
  formatting-only JSON saves. Structured dialogs use shared flat property rows.
- All seven NATS/JetStream types and both Serial frame types now render inline
  through `OptionPropertyRows`, reusing `OptionEditorField` and existing metadata.
- Provisioning fields are conditional on Create stream/Create consumer; Serial
  delimiter and frame length follow Framing. Hidden values are retained.
- Advanced JSON remains available and opens in JSON mode. Existing visual mode
  and existing configuration validators remain available there.
- Shorter labels change display metadata only, not serialized property names.
- 51 focused tests passed on each of .NET 8 and .NET 10. Coverage includes all nine
  inline transport types, hidden values, casing/unknown fields, cancellation,
  stale dialog results, no-op saves, Undo/Redo and project save/reopen.
- The metadata coverage test's Publisher exclusions now correctly account for
  Consumer-only ACK/NACK and RequeueOnNack on the shared RabbitMQ DTO.
- Release web publish succeeded. Existing build warnings remain.
- Browser trial in a separate temporary project: JetStream provisioning switches,
  Serial framing, numeric editing, keyboard use, Cancel, Undo/Redo, Advanced JSON
  to Visual switching, and an 800-pixel-wide dialog. Original tabs were preserved.
- At this checkpoint the advanced dialog still listed all metadata fields rather
  than applying the Inspector's conditions. The later conditional transport-dialog
  batch resolves that discrepancy. Full per-field validation and reference audits
  remain; these nine rows are not yet claimed as final whole-feature acceptance.

### Visual collection batch

- HTTP Methods uses the shared string collection editor with Add/Edit/Remove and
  existing HTTP method validation. Custom tokens, saved spelling and omitted POST
  defaults are preserved. Non-array or mixed saved values retain a JSON fallback.
- HTTP response status and reply timeout follow Response mode without deleting
  hidden values. Unknown modes expose both settings for repair.
- Router routes now have visual Add/Edit/Remove. Existing output names remain
  fixed when editing expressions; removal warns that saved wiring remains.
  Unknown fields, source strings and saved links survive edits and Undo/Redo.
- 64 focused tests passed on both .NET 8 and .NET 10, including the preceding
  transport batch, dialog validation, cancellation, stale results, no-op edits,
  project save/reopen and metadata coverage. Existing unrelated warnings remain.
- Browser trial verified HTTP custom methods, removal/Undo, response conditions,
  Router port creation, expression-edit Cancel, removal confirmation/Undo, and an
  800-pixel-wide dialog. A separate temporary project preserved existing tabs.
- No runtime, Core or WFENG changes were required.

### ROS QoS visual editor

- Publisher and Subscriber share one configurator in the ROS feature folder and
  reuse `OptionPropertyRows`/`OptionEditorField`. All nine `Rcl.Qos.QosProfile`
  fields are visible, with optional Advanced JSON for the complete saved object.
- The installed Rcl.NET 2.1.2 contract and documentation were inspected locally.
  Omitted/null and empty object QoS use `QosProfile.Default`; a nonempty saved
  profile uses constructor defaults for omitted fields. This distinction was
  verified against the runtime configuration binder during family acceptance.
- Existing numeric enum values, key casing and unknown fields remain unchanged
  when another field is edited. Unsupported shapes and duplicate field names
  retain JSON editing rather than being silently rewritten.
- Eight new regressions cover both component types, omitted/null/empty/custom
  profiles, unknown fields, missing resources, Undo/Redo and save/reopen.
- The combined 72-test editor/metadata regression set passed on .NET 8 and .NET 10;
  existing unrelated warnings remain. Release web publish also passed.
- The published browser trial found a trimming failure in reflection-based
  serialization of the external profile record. Explicit mapping of its supported
  fields fixed the failure without adding runtime dependencies or changing Core.
- Browser verification covers default display, keyboard numeric editing, policy
  selection, synchronized JSON, Cancel, Undo/Redo, component switching and a narrow
  Inspector at an 800-pixel viewport. The latest app remains open separately.
- This is configuration/UI evidence, not native ROS middleware execution evidence.
  The duration, history/depth and queue-choice follow-ups are addressed below.
  Do not claim whole-feature acceptance yet.

### Shared durations and ROS conditions

- `DurationField` supplies flat TimeSpan input through the existing metadata
  editor. Invalid input stays in the draft; the previous configuration and redo
  remain intact. Its compact error action reveals the existing Validation record.
  Correcting the input or disposing the editor clears that transient diagnostic.
- Valid input keeps its spelling, including fractional seconds and negative
  TimeSpans used by existing timeout contracts. Empty input removes the optional
  setting. No runtime-specific duration constraints or defaults were changed.
- ROS QoS now uses this editor for Deadline, Lifespan and Lease duration. Depth
  hides only for explicit KeepAll, including its numeric representation; the saved
  value is retained. Unknown history modes leave it visible for repair.
- ROS Subscriber offers supported drop modes. Existing Wait/0 settings remain
  visible as unsupported rather than being rewritten. Saved unknown queue modes
  remain selectable for inspection. The Ignore local label was shortened only.
- Initial-render regressions cover actual field values as well as edits, invalid
  drafts, linked diagnostics, saved casing, Undo/Redo and save/reopen. The browser
  trial also checked correction back to the original value preserving redo.
- Browser checks passed for KeepAll/Undo, supported queue choices, invalid and
  corrected duration drafts, Validation navigation, and an 800-pixel viewport.
  The updated app is open separately; original tabs were preserved.
- All 448 shared Studio hosting/editor and component metadata tests passed on
  both .NET 8 and .NET 10. Release web publish passed; existing warnings remain.
- Runtime, Core and WFENG were not modified.

### Expression editor lifecycle

- The shared expression field now rejects saves after a project revision,
  selected node/link, engine, condition mode or source change, and after disposal.
  Repeated open actions cannot create competing dialogs. Saved expression text
  remains exactly as returned by the editor; preview formatting is not persisted.
- Nine regressions cover changed targets/context, disposal, cancellation, no-op
  saves, re-entrant opening, source preservation, Undo/Redo and save/reopen.
- All 489 shared hosting, component metadata and expression evaluation tests
  passed on .NET 8 and .NET 10. Release web publish passed. The existing web-host
  package advisory remains; dependencies were not changed in this UI batch.
- A separate browser project verified Close discards expression edits, live
  mapper evaluation, Save, Undo and Redo. Cancel after Undo preserved Redo.
  The updated application is open separately and existing tabs were preserved.
- Data Generator and Reducer were audited against the current evaluator. It
  evaluates mapper/condition message contexts, not generator functions or reducer
  state/equality contexts. Their source editors still need shared visual editing
  with accurate context support; do not route them through a misleading mapper
  preview. Reducer Initial state remains a JSON source, distinct from expressions.
- Runtime, Core and WFENG were not modified. This batch does not close the
  remaining component-family or whole-application acceptance work.

### Generator and reducer expression sources

- Data Generator Expression and Reducer Reduce/Equality now use the shared
  expression field and code editor rather than generic multiline text dialogs.
  The Inspector displays a read-only source preview and the shared edit action.
- Description metadata selects source-only editing when live evaluation is not
  supported for the component's context. The same dialog omits sample/result
  panels and makes runner evaluation explicit; it never calls the mapper preview.
  Existing mapper and condition editors retain their live evaluation behavior.
- Generator syntax comes from metadata without adding an Engine configuration
  setting. Reducer uses its saved Engine. Stored paths, base64 sources, whitespace,
  casing and unrelated settings remain intact through Save/Cancel and history.
- Initial state remains a JSON source rather than being treated as an expression.
  Its source editor and the remaining family validation are still follow-up work.
- Twenty focused expression tests passed on both frameworks. The broader 500-test
  hosting, metadata and evaluation suite also passed on .NET 8 and .NET 10.
  Release publish passed with existing warnings.
- Browser checks verified generator and reducer saves, equality Cancel, Undo/Redo
  and the source-only dialog at 800 pixels. The initial fixed-height code area
  left unused space; using the existing shared text-editor layout fixed it.
  No new layout rules or runtime evaluator were introduced.
- The updated application remains open in a separate tab. User tabs, runtime,
  Core and WFENG were not modified.

### JSON source settings

- Source Values and Reducer Initial state share an explicit Inline JSON/File
  editor, using the existing code editor and dialog shell. Source requires a JSON
  array; Reducer permits any JSON value. Inspector actions indicate JSON or File.
- Classification follows the existing JsonSource contract. Unchanged source text
  retains its original prefix, casing and whitespace. Changed values use explicit
  prefixes, so a numeric file name cannot silently become a JSON number.
- File references are never opened by the editor. Both mode drafts survive
  switching; invalid JSON remains a draft. Runtime file resolution is unchanged.
- The existing stale-target and history guards apply to both fields. Tests cover
  Cancel, unchanged Save, changed graph revision, unknown fields, mixed payload
  types, Undo/Redo and project save/reopen.
- All 525 shared hosting, metadata, expression and JSON-source tests passed on
  each of .NET 8 and .NET 10. Release publish passed with existing warnings.
- Browser checks verified array validation, retained drafts, Save/Cancel,
  Undo/Redo, scalar null initial state and both dialog modes at 800 pixels. File
  mode uses the compact shared dialog size; JSON mode fills the editor layout.
- The updated application remains open separately. No runtime, Core or WFENG
  edits were required. This is not a visual collection editor or a runner-file
  execution trial; those claims are not covered by this batch.

### Scheduler audit follow-up

- Registration requires exactly one of Interval, Cron and At. TimeZone and a true
  IncludeSeconds are rejected outside Cron. A future schedule-mode editor must
  account for that contract: simply hiding retained Cron settings would leave an
  invalid runnable configuration. Do not add a serialized mode or change runtime
  validation to simplify the UI. Existing conflicting values must remain visible
  for repair and mode changes must preserve history.

### Scheduler fields and shared validated text

- Scheduler keeps Interval, Cron and At visible, without introducing a mode setting
  or deleting incompatible saved values. Time zone and Include seconds appear for
  Cron or when saved Cron-only settings need inspection/repair. Null settings do
  not expand the form; saved false and unknown values remain accessible.
- At uses shared validated text with a compact link to the existing Validation
  tab. Invalid drafts do not change configuration or redo. Valid date/time strings
  retain their exact spelling, fractional precision and offset; no local-time
  conversion is written back. Empty input removes the optional field.
- Duration now reuses the same draft and diagnostic component. Its parsing and
  source-preservation behavior remain unchanged. The obsolete duration-only layout
  stylesheet was replaced by the shared field stylesheet.
- Labels are shorter, and Output capacity uses the existing runtime minimum of
  one. Time zone remains manually editable because runner identifiers may differ
  from the browser host. Full cron/time-zone checks remain in existing validation.
- Fourteen new regressions cover conditional settings, saved conflicts, invalid
  date drafts, original offsets, linked validation, no-op edits, Undo/Redo and
  save/reopen. All 539 shared hosting, metadata, expression and JSON-source tests
  passed on .NET 8 and .NET 10. Release publish passed with existing warnings.
- Published-browser checks verified Cron fields appearing, retained time zone
  after Cron removal, field hiding after repair, invalid-date navigation,
  correction, Undo/Redo and an 800-pixel Inspector. The updated app remains open
  separately. No runtime, Core or WFENG changes were made.

### Typed component expression previews

- Change Filter and Deduplicate compile string key selectors; Retry compiles a
  Boolean predicate. Their shared editor now requests those same native return
  types instead of evaluating every expression as a mapper FlowValue.
- Optional authoring metadata supplies the result kind. The shared evaluation
  request carries it to the existing host endpoint, and the expression panel names
  the expected type. Existing mapper requests default to Value. Link conditions
  retain their raw-expression and default-engine behavior.
- Component previews still use the native inline/base64 source reader, require an
  explicit engine and reject file access. No runtime component or configuration
  contract changed. A changed result kind invalidates a pending editor save.
- Twelve regressions cover native string/Boolean evaluation with both JSONata and
  C#, wrong-type results, source reading, the HTTP request boundary, metadata
  forwarding, unchanged source and stale dialog context. All 551 shared hosting,
  metadata, expression and JSON-source tests passed on each of .NET 8 and .NET 10.
- Both web UI and preview host were published. Existing warnings remain. Browser
  checks verified numeric-key rejection, string-key success, Boolean Retry source,
  Save/Cancel, Undo/Redo and the result-type label at 800 pixels.
- Retry's preview remains a normal message sample, not a failed-reply simulation;
  its help states that limitation. This batch does not claim complete runtime
  retry-policy simulation or final whole-component acceptance.
- The updated application remains open separately. Runtime, original telemetry,
  Core and WFENG were not modified.

### Visual HTTP connection headers

- HTTP connection Headers now uses a shared named-string collection group with
  Add/Edit/Remove and optional Advanced JSON. Header names remain visible in the
  grid; values are masked in the entry dialog because custom headers may contain
  credentials. Exact names, values, property casing and unrelated settings survive
  edits. Unsupported saved shapes remain accessible through JSON without conversion.
- Validation uses the native HTTP request-header API, including content-header and
  invalid-value rejection. Duplicate names are checked case-insensitively. Opening
  the editor performs no HTTP requests. Existing runtime configuration is unchanged.
- Integration tests caught an edit guard blocking parent connection commits. The
  guard now ends before the existing commit API, matching the shared JSON editor.
  Browser verification caught disappearing feedback after consecutive invalid drafts;
  the dialog now uses the shared form validation lifecycle, with regression coverage.
- Fourteen new tests cover edits, Cancel, unchanged Save, duplicate/invalid names,
  stale/disposed editors, unsupported shapes, validation recovery, credentials,
  casing, unknown fields, Undo/Redo and save/reopen. All 565 shared hosting,
  metadata, expression and JSON-source tests passed on each of .NET 8 and .NET 10.
  Release publish passed. Build warnings remain; this is not a dependency audit.
- Browser checks verified Add/Edit/Remove, successive validation errors and repair,
  Cancel preserving Redo, Undo/Redo, Advanced JSON value preservation and an
  800-pixel dialog. The updated application is open separately on port 5496.
- Authentication conditions and final whole-connection acceptance remain in the
  inventory. No runtime, telemetry, Core or WFENG edits were made in this batch.

### Conditional HTTP authentication fields

- HTTP authentication now selects Basic, Bearer or OAuth client-credentials fields
  through an optional shared group visibility predicate. The predicate reads the
  current draft, so field changes update the grid without rebuilding unrelated
  editors or introducing configuration properties.
- The native runtime rejects mixed-scheme credentials. Saved non-null fields remain
  visible for explicit repair, including empty strings and unexpected value types.
  Unknown schemes show all known fields. Absent/null schemes show the selector and
  any retained settings. No hidden-value deletion or default insertion occurs.
- Twelve regressions cover scheme casing, null/unknown settings, retained conflicts,
  live switching, field removal, Reset, secret masking, mixed-case property keys,
  unknown fields, Undo/Redo and save/reopen. All 577 shared hosting, metadata,
  expression and JSON-source tests passed on each of .NET 8 and .NET 10.
- Published-browser checks verified Basic/Bearer/OAuth switching, retained username
  repair, Reset and Undo/Redo at normal and 800-pixel widths. The updated application
  remains open separately on port 5497. This was an editor trial, not a live
  authentication exchange. No runtime, Core or WFENG changes were made.
- Existing shared text semantics remove empty optional input; explicit empty
  credential values can still be preserved/entered through Advanced JSON. Broader
  connection acceptance must address that distinction before claiming complete
  visual coverage of every authentication value.

### Explicit empty HTTP Basic password

- The shared text editor now has opt-in `AllowEmptyText` metadata, enabled for the
  HTTP authentication Password field. It preserves the native distinction between
  an absent password and a valid empty string. Set empty creates the string; Reset
  removes it; clearing non-empty text stores an empty string. The cell shows Empty
  or Not set without exposing the value. Other fields keep their existing semantics.
- Empty, null, missing and non-empty saved values remain unchanged on render and
  unchanged input. Disposed text actions reject stale callbacks. Existing graph
  commits preserve property spelling, credentials, unknown fields and history.
- Four regression cases exercise all starting shapes, creation/removal, stale reset,
  unchanged input preserving Redo, masking, Undo/Redo and save/reopen. All 581 shared
  hosting, metadata, expression and JSON-source tests passed on each of .NET 8 and
  .NET 10. Release publish passed with existing warnings.
- Published-browser checks verified exact empty-string JSON, Reset, Undo, keyboard
  actions and the flat cell at 800 pixels. The updated application remains open
  separately on port 5498. No live authentication exchange was attempted.
- This closes the explicit-empty-password visual gap recorded above. No runtime,
  telemetry, Core or WFENG changes were made; final whole-connection acceptance and
  the remaining inventory still apply.

### BLE GATT Server ordinary settings

- Adapter connection, Connectable, Discoverable and Output capacity now use the
  shared Property Grid rows and metadata. Capacity matches the existing positive
  integer runtime setting, with a default of 128. Opening the grid does not insert
  defaults, replace missing adapters or migrate legacy service configuration.
- The specialized hierarchy editor retains services, characteristics, descriptors,
  templates and Visual/JSON switching. Its duplicate advertising form was removed.
  Advanced JSON opens the same editor directly in JSON mode with the original text.
- Dialog saves reject stale selections/configuration and disposed editors. A busy
  guard prevents duplicate openings; disposal closes an outstanding dialog. Semantic
  equality prevents object-property reordering alone from consuming history.
- Eight regressions cover legacy and multi-service preservation, missing references,
  property spelling, unknown fields, flat controls, capacity edits, Cancel, semantic
  no-op, stale/disposed dialogs, Undo/Redo and save/reopen. The selected shared hosting,
  metadata, expression, JSON-source and BLE server editor suites passed all 602 tests
  on each of .NET 8 and .NET 10. Release publish passed with existing warnings.
- Published-browser checks verified inline values in JSON, visual service/characteristic
  creation, Save, Cancel preserving Redo, Undo/Redo, keyboard opening and the Inspector
  and hierarchy dialog at 800 pixels. The updated app is open separately on port 5499.
- This was a configuration-editor trial; no radio operations or live GATT execution
  were attempted. Final hierarchy-wide acceptance remains. Runtime, original telemetry,
  Core and WFENG were not modified.

### Structured BLE connection targets

- BLE peripheral connections now expose Device address and Address type as flat
  grid fields. They project the existing Endpoint property; no presentation-only
  fields enter configuration. Native BLE APIs validate addresses and endpoints.
  Equivalent case/separator edits preserve original endpoint spelling and Redo.
- BLE adapter connections expose only the execution host's supported Default
  choice. Valid saved variants remain untouched; unsupported saved endpoints remain
  visible until explicitly replaced. Invalid peripheral endpoint text can be repaired
  inline; non-string saved values remain available through Advanced JSON.
- The shared connection grid accepts optional feature-owned group rows, retaining
  its existing commit and stale-edit checks. The BLE rows and description are
  defined by the Bluetooth plugin and rendered by the shared connection grid. Unknown fields, property spelling,
  native configuration, Undo/Redo and save/reopen remain intact.
- Shared diagnostics without a component address now show an accessible error icon
  and explanation rather than disappearing. They do not create a false component
  Validation record. Component-linked diagnostic behavior remains unchanged.
- Eleven new regression cases cover valid and invalid saved endpoints, equivalent
  addresses, type changes, stale callbacks, explicit adapter repair, Undo/Redo and
  save/reopen. All 613 selected hosting, metadata, expression, JSON-source and BLE
  server editor tests passed on each of .NET 8 and .NET 10. Browser verification
  then found literal text bindings; those were corrected and display-value assertions
  added. All 12 focused BLE connection tests passed on both frameworks afterward.
- Release publish passed with existing warnings. Browser checks verified displayed
  values, invalid draft retention, address repair, endpoint JSON, Cancel preserving
  Redo, Undo/Redo, adapter selection, keyboard selection and 800-pixel layout.
  The updated app remains open separately on port 5500. No radio scanning, pairing
  or connection was attempted; runtime, telemetry, Core and WFENG were not changed.

### Conditional transport dialogs

- NATS/JetStream provisioning and Serial framing now share one visibility predicate
  between their Inspector and the structured dialog's Visual mode. The shared
  dialog reevaluates against its own draft after edits and JSON-to-Visual switching.
  Hidden values are retained; unrelated fields are not recreated on each edit.
- Unsupported controlling values leave dependent fields accessible for repair.
  Serial numeric enum values outside the supported framing choices no longer hide
  both framing settings. Rendering does not normalize or replace these saved values.
- Numeric and checkbox shared fields reject callbacks after disposal, matching
  existing text-field behavior. This prevents a removed conditional editor from
  modifying its old draft.
- Eight new regression cases exercise the actual component dialogs for JetStream
  Publisher/Consumer and Serial Reader/Writer, including immediate visibility,
  hidden data, late numeric callbacks, invalid JSON, mode switching, Save/Cancel,
  property spelling, unknown fields, Undo/Redo and save/reopen.
- All 621 selected hosting, metadata, expression, JSON-source and BLE server editor
  tests passed on each of .NET 8 and .NET 10. Release web publish succeeded; existing
  build warnings remain, including the WebAssembly.Server package advisory.
- Browser checks in a fresh temporary project confirmed keyboard toggling,
  consumer settings, framing changes, retained numeric values in JSON, Cancel and
  an 800-by-720 layout. The updated app is open separately on port 5501; existing
  tabs were preserved. No broker, serial, runtime, Core or WFENG changes were made.
- Numeric precision, delimiter authoring, full validation and the remaining
  inventory acceptance work remain open.

### Exact integer settings

- Confirmed that the previous double-backed number editor rounded JetStream's
  Int64 retention counts above 2^53. Max messages and Max stream bytes now use the
  shared Integer editor in both Inspector and structured Visual/JSON dialogs.
  Configuration still contains the same native properties and JSON integer values.
- The editor uses the existing flat validated field and diagnostics. It preserves
  exact Int64 values, rejects fractional/overflow/out-of-range drafts, and retains
  numeric strings and their spelling on equivalent edits. Missing defaults, saved
  nulls, empty strings, unknown fields and property casing remain unchanged until
  an explicit edit. Clearing an explicit nonempty value removes the property.
- Nine new regression cases cover both JetStream operations and both properties,
  adjacent values above 2^53, Int64.MaxValue, invalid drafts, numeric strings,
  bounds, defaults, nulls, empty values, disposal, Undo/Redo and save/reopen.
  Existing transport dialog tests now round-trip a value above 2^53 through
  conditional visibility, Visual/JSON modes and Save/Cancel.
- The broader 629-test selection passed on .NET 8 and .NET 10. Final review then
  added the unchanged-null/empty guard and an additional regression case; all 39
  focused integer/transport tests passed on both frameworks afterward. Release web
  publish succeeded with build warnings remaining.
- Browser verification in a fresh temporary project confirmed exact display and
  JSON for 9007199254740993 and 9223372036854775807, overflow draft retention,
  recovery, Undo/Redo, Visual/JSON switching, Cancel and the 800-by-720 dialog.
  The updated application remains open separately on port 5502. Runtime, broker
  behavior, original telemetry, Core and WFENG were not modified.
- The known JetStream retention precision gap is resolved. Other numeric contracts
  and full per-feature acceptance still require review. The next batch addresses
  structured-dialog handling of uncommitted invalid field drafts; this is not
  completion of the overall inventory.

### Visual draft validation

- Confirmed that structured dialogs previously saved the last valid configuration
  even when a visible field held invalid uncommitted text, and switching to JSON
  silently discarded that text. The existing MudForm now validates the live Visual
  editors before Save or mode switching. Invalid drafts stay open for correction;
  Cancel continues to discard the dialog draft without changing the project.
- ValidatedTextField now participates in native form validation. The existing
  compact diagnostic remains the visible error surface; the duplicate form helper
  is suppressed. Icons retain their Validation link and expose the explanation in
  a tooltip and accessible description. No new validation registry was introduced.
- Hidden conditional editors unregister from the form. Their last committed
  configuration is retained and still checked by the feature's native save validator.
  Other valid draft changes remain intact while an invalid field is corrected.
- Eight new regression cases cover integer, duration and date/time drafts,
  overflow, blocked Save/mode switching, correction, Cancel, unknown fields,
  retained Redo and removal of conditional field errors. The existing BLE diagnostic
  test now distinguishes visible error paragraphs from accessible tooltip text.
- The broader 638-test selection passed on .NET 8 and .NET 10; the final tooltip
  behavior was also covered by 70 focused tests on each framework. Release publish
  succeeded with build warnings remaining.
- Browser checks in a fresh temporary project confirmed blocked Save/JSON switching,
  retained invalid text, correction, preserved unknown data, Save and Undo/Redo.
  An 800-by-720 screenshot exposed a library flex utility overriding helper hiding;
  the scoped rule was corrected and the compact layout reverified after publishing.
  The updated app remains open separately on port 5503. Existing tabs were preserved.
  Runtime, telemetry, configuration contracts, Core and WFENG were not changed.
- Remaining inventory items and final per-feature acceptance remain open.

### Serial delimiter editors

- Audited the actual Serial contracts: Serial Text accepts only LF or CRLF;
  Reader/Writer delimiter framing accepts 1 to 64 arbitrary bytes. Serial Text
  now uses the existing selector for its two supported line endings instead of
  opening a JSON editor. Its saved NewLine remains an actual string.
- Reader/Writer reuse a shared Base64 field with LF, CRLF and CR presets plus a
  custom Base64 input. Presets and byte limits come from authoring metadata;
  the shared field has no Serial runtime dependency. The input reuses the flat
  validated field and compact diagnostics in both Inspector and Visual dialog.
- Viewing defaults, choosing Custom, or entering an equivalent byte sequence
  does not modify configuration or redo history. Existing Base64 whitespace,
  property casing, invalid saved values and unknown fields survive until an
  explicit change. Invalid custom drafts remain editable. Disposed fields reject
  late callbacks; the existing conditional framing and JSON paths remain intact.
- Eight regression cases cover omitted defaults, text line endings, arbitrary
  binary bytes, exact saved spelling, invalid/empty/null values, delimiter bounds,
  stale callbacks, Undo/Redo and save/reopen. Existing Reader/Writer dialog tests
  also cover invalid Base64 blocking JSON switching, correction without
  normalization, hidden-value retention and Save/Cancel.
- The broader 646-test selection passed on both .NET 8 and .NET 10. Release
  publish succeeded with build warnings remaining. Browser checks in a temporary
  project verified keyboard selection, invalid custom input, blocked Save/JSON,
  Cancel, Undo/Redo and the dialog at 800 by 720 pixels. The updated application
  remains open on port 5504; existing user tabs were preserved.
- Runtime contracts, hardware access, configuration formats, Core and WFENG were
  unchanged. Cross-field Serial validation and the remaining inventory are still
  open; this batch does not complete the application-wide goal.

### Integral settings and connection editing continuity

- Audited the remaining 68 floating-point metadata declarations against their
  integral configuration contracts. Counts, capacities, ports, status codes and
  integer timeouts now use the existing flat Integer editor with native bounds.
  Existing tighter feature limits remain, including UInt16 prefetch, UInt32 MQTT
  session expiry and Serial Text's maximum line length below Int32.MaxValue.
  The Number editor remains available for genuinely fractional contracts.
- Required integers reject empty drafts; optional clearing retains the existing
  removal behavior. Omitted-default hints remain visible. Equivalent edits retain
  saved numeric-string spelling, property casing and unknown settings.
- Fourteen numeric-contract cases exercise actual feature metadata, fractions,
  overflow, bounds, required values and exact writes. Contract coverage also checks
  that registered integral properties use an integer editor and suitable bounds.
- Browser acceptance exposed two connection editing gaps. Add connection now ends
  the preceding component Inspector edit before opening its own transaction.
  Page Undo/Redo retains the active connection Inspector when that resource still
  exists, rather than letting restored canvas selection replace it. Removed or
  replaced resources still clear selection. Regression coverage exercises actual
  page actions, credentials, cancellation, stale dialogs and connection removal.
- The final 661-test editor selection passed on .NET 8 and .NET 10. Release publish
  succeeded. Existing build warnings remain; this batch does not change dependencies.
- Browser checks in a separate temporary project verified Add connection after a
  component edit, UInt32 overflow rejection and correction, exact Advanced JSON,
  Undo/Redo with retained connection selection, an 800-by-720 layout and save/reopen.
  The saved session expiry reopened as 4294967295. The updated app remains on port
  5505, with the user's existing tab preserved and the viewport override reset.
- Runtime behavior, configuration contracts, original telemetry, Core and WFENG
  were unchanged. Cross-field validation and the remaining per-feature acceptance
  items remain open; this is not completion of the application-wide inventory.

### Serial cross-field validation

- The Serial Reader/Writer Inspector now explains the native read-buffer and
  fixed-frame limits beside their fields. Both relationships use the existing
  SerialFrameOptions validator; no runtime rule or saved setting was changed.
  Feature validation stays beside SerialFrameConfigurator. Shared rows accept
  messages and render the existing PropertyDiagnostic, linking committed settings
  to Validation while dialog drafts use local, accessible diagnostic icons.
- Invalid saved combinations remain editable without normalization or automatic
  resizing. Repairing the maximum, repairing the dependent field or switching
  framing modes refreshes the diagnostics. Hidden frame lengths and delimiter
  bytes remain intact. Undo/Redo restores both configuration and diagnostics.
- Structured dialogs reuse the same field validation before Save, in Visual or
  JSON mode. A browser check exposed raw exception resource keys in the published
  app; known field failures now use the readable diagnostic message. Changing a
  draft clears the prior Save error, and the next Save validates again.
- Six new cases cover both Serial types, native cross-field errors, correction,
  mode switching, Visual/JSON, blocked Save, Cancel, retained Redo, unknown fields,
  property casing, original Base64 spelling and history/save-reopen preservation.
  The broader 667-test editor selection passed on .NET 8 and .NET 10. After the
  readable Save-message refinement, 32 focused tests passed on both frameworks.
  Release publish succeeded with existing build warnings remaining.
- Browser acceptance used a temporary project with an unused COM99 connection;
  no port was opened. It verified Validation navigation, dependent-error updates,
  framing changes, correction, Cancel, blocked Save, readable errors, Visual/JSON
  preservation, Undo/Redo and an 800-by-720 dialog. The viewport was reset and the
  updated application remains open separately on port 5506.
- Core, WFENG, runtime behavior, telemetry and the user's existing projects were
  unchanged. Final family acceptance and the other inventory items remain open.

### Duration bounds and Batch/Gate acceptance

- Shared duration fields now consume optional bounds and required-value metadata.
  Batch waits and Request-Reply timeouts must be positive and within the native
  timer limit. Rate Limit additionally permits its existing infinite timeout.
  No saved text, defaults, runtime contracts or unrelated duration rules change.
- Nine duration cases exercise registered feature metadata, required values,
  zero/negative/oversized drafts, exact bounds, infinite timeout support, invalid
  saved values, original text/property spelling, unknown values and Undo/Redo.
  A Gate case covers its omitted false default, no-op edits, explicit true,
  history and save/reopen. The 677-test editor selection passed on both .NET 8
  and .NET 10; existing build warnings remain.
- Browser acceptance in a separate temporary project verified Batch's linked
  zero-wait diagnostic and repair, all Batch and Gate fields, Gate Undo/Redo,
  both Inspectors at 800 by 720 pixels, and actual save/reopen. Batch retained
  the exact text `00:00:05.2500000`, size 2 and output capacity 64; Gate retained
  its true initial state and capacity 32. Neither component was executed.
- Batch and Gate configuration-editor acceptance is complete. Request-Reply's
  combined capacity constraint and Rate Limit's host-owned limiter selector
  require further work; the broader inventory remains open.
- The verified application remains available on port 5507. The temporary viewport
  override was reset and existing user tabs were preserved. Core, WFENG,
  runtime behavior, original telemetry and configuration contracts were unchanged.

### Request-Reply capacity relationship and limiter resource audit

- Request-Reply's existing description now supplies each capacity field's upper
  bound from the other committed value, including omitted defaults. The two
  fields cannot accept a combined capacity above Int32.MaxValue. Either field
  can repair an invalid saved combination; values are never clamped or rewritten
  on opening. The shared integer field also validates a restored default before
  clearing an optional explicit value.
- Ten regression cases cover both repair directions, exact limits, default/null
  handling, zero buffering, rejected clearing, original numeric-string spelling,
  unknown fields and Undo/Redo/save-reopen. Boundary decisions are checked against
  the actual registered ComponentConfigurationCheck rather than only against
  the metadata implementation. The broader 687-test selection passed separately
  on .NET 8 and .NET 10. Release publish succeeded; existing warnings remain.
- Browser acceptance verified rejection of excessive combined capacity, the
  linked Validation diagnostic, exact-limit acceptance with zero buffer, rejected
  default restoration, correction, timeout edits, keyboard selection of Continue,
  Undo/Redo and an 800-by-720 Inspector. Actual save/reopen retained MaxInFlight 4,
  BufferCapacity 0, ReplyTimeout `00:00:10.0000000` and TimeoutBehavior Continue.
  The trial did not execute the workflow or allocate the tested upper capacities.
- Rate Limit's Limiter resolves through GetRequiredFlowResource<RateLimiter>,
  including an explicit host key. WorkflowRuntimeCheck already reports that the
  generic companion has no limiter factory. LocalCompanionEndpoints supplies no
  discovery endpoint for these objects, and project resource descriptions do not
  define a limiter connection type. Retain manual host keys; do not substitute a
  project connection dropdown or invent a new runtime resource contract in this
  UI phase. Final manual-field acceptance remains tracked in the table.
- The updated application remains open separately on port 5508, with viewport
  overrides reset and user tabs preserved. Core, WFENG, runtime semantics,
  configuration contracts and original telemetry were unchanged. The broader
  harmonization goal remains in progress.

### OTLP logging resource editor

- Registered the existing logging.otlp resource in Add connection. Its description
  lives in the Connections/Logging slice and uses the shared connection Inspector,
  Property Grid, selectors, duration field and optional Advanced JSON. No new
  runtime resource or exporter behavior was introduced.
- Collector and Logging groups expose all seven OtlpLoggerConfiguration settings.
  Headers use a masked text field and retain the native comma-separated format,
  including '=' inside values. Protocol changes do not rewrite the endpoint or
  other settings. The minimum-level selector uses the standard LogLevel values.
- Nine regressions verify native-property coverage, actual registration validation
  at the timeout bounds, preservation of numeric enums until explicitly changed,
  header text, unknown fields, key spelling, invalid drafts, Undo/Redo, optional
  and missing Logger references, compatible choices and save/reopen. Resource
  registration checks do not instantiate an exporter or contact a collector.
  The broader editor selection passed 710 tests on each of .NET 8 and .NET 10.
  Release web publication succeeded; existing warnings remain.
- Browser acceptance created an application-scoped OTLP connection, edited all
  seven settings, repaired an invalid timeout, checked masked headers and a narrow
  Inspector, and verified Advanced JSON Cancel retaining Redo. Logger selected the
  new connection through its existing typed selector. Actual save/reopen retained
  the reference, endpoint, protocol, exact timeout text, header value and logging
  settings. The trial did not execute the workflow or send telemetry.
- The updated application remains open separately on port 5510 with the viewport
  reset and existing tabs preserved. Endpoint/header validation and structured
  header entry remain tracked above; this is not final whole-Logger acceptance.
  Core, WFENG, runtime behavior and original telemetry capture were unchanged.

### Required text and Rate Limit/HTTP Client acceptance

- Required plain-text metadata fields now use the shared ValidatedTextField.
  Empty and whitespace-only drafts retain the last committed configuration and
  redo history while reporting through the existing diagnostic. Valid text is
  retained exactly. Optional fields, explicit-empty settings and typed connection
  selectors keep their existing behavior.
- Eight regression cases cover required fields across Rate Limit, Router and
  NATS Subscriber, plus HTTP Client's compatible, missing and wrong-type saved
  references. They verify no-op edits, original text/key spelling, unknown fields,
  Undo/Redo and save/reopen. The broader editor selection passed 701 tests on each
  of .NET 8 and .NET 10; both TRX reports show zero failed or skipped tests.
- Browser acceptance verified required-field diagnostics and repair, Rate Limit
  settings, HTTP-only resource choices with a disabled missing reference, Escape
  cancellation, keyboard selection and reference Undo/Redo. Both Inspectors were
  checked at narrow widths. Actual save/reopen retained the limiter key,
  `00:00:05.0000000` timeout and capacity 16; HTTP retained `Resources.api` under
  its original lowercase `client` key and capacity 48. Unknown fields and both
  original resource definitions remained intact. No workflow was executed.
- Release web publication succeeded. The updated application is available on
  port 5509; the temporary viewport override was reset and prior tabs preserved.
- Logger resource inspection confirmed that LoggerRegistrationExtensions already
  registers logging.otlp with seven configuration properties. At that checkpoint
  Studio had no corresponding ConnectionDescription. The OTLP resource-editor
  batch above supplies visual creation; its remaining validation gaps stay tracked
  rather than claiming whole-Logger acceptance. Core, WFENG, runtime behavior and original
  telemetry were unchanged. The application-wide inventory remains open.

### OTLP header collection and endpoint validation

- The existing shared string-map editor now accepts paired feature-owned read and
  write adapters for native encoded collections. Ordinary JSON-object maps keep
  their existing behavior. OTLP owns its comma-separated header conversion in the
  Connections/Logging slice; storage remains a string, not a projected dictionary.
- Headers use the shared Add/Edit/Remove dialog with masked values. Unchanged
  entries retain their original encoded text when another entry changes. New or
  explicitly changed entries escape commas, equals signs and percent characters.
  Cancel and no-op edits retain the original string and redo history. Duplicate
  names, unsupported shapes and values that cannot be safely edited visually
  remain available through Advanced JSON without being flattened or discarded.
- Endpoint uses the shared validated text field. Invalid visual drafts cannot
  replace the saved endpoint; valid HTTP(S) addresses retain their exact text.
  Header validation errors do not echo secret values. These validations apply to
  visual fields; full semantic validation of Advanced JSON remains outstanding.
- Twenty-one added cases cover collection actions, invalid and duplicate entries,
  stale/disposed editors, endpoint validation, original text/key spelling,
  unknown fields, Undo/Redo and save/reopen. The focused selection passed 44 tests;
  the broader selection passed 731 on each of .NET 8 and .NET 10, with zero failed
  or skipped tests in both TRX reports. Release web publication succeeded.
- Browser acceptance used a separate temporary project and the shared dialogs.
  Adding an encoded header, cancelling an existing-header edit, Undo/Redo and an
  800-by-720 viewport were verified. Actual save/reopen retained the original
  X-Trial value and the exact encoded X-Encoded value, plus the endpoint, protocol,
  timeout, logging settings and Logger resource reference. No workflow was run
  and no telemetry was sent to a collector.
- The updated application remains available on port 5511. The viewport override
  was reset and prior user tabs preserved. Core, WFENG, runtime behavior and
  original telemetry were unchanged. The broader inventory remains open.

### Logger category and connection JSON draft validation

- Plain-text metadata now accepts a feature-owned validation callback and uses
  the existing validated field and linked diagnostic. Logger rejects whitespace-only
  Category drafts, retains existing saved text until repair, preserves valid text
  exactly, and allows clearing the optional value to restore the component-address
  default. No runtime logging behavior or captured record was changed.
- The shared JSON dialog accepts a semantic object validator. Connection Inspector
  supplies its existing Type and description checks before the dialog closes.
  Invalid syntax, object shape, Type or supported setting values keep the original
  draft open for correction or Cancel. The final commit retains revision/selection
  checks and validates again before changing the graph.
- OTLP's feature-local validator binds the existing configuration model and checks
  endpoint, protocol, log level, timeout and the complete native header list.
  Repeated custom headers remain valid in JSON when the native registration accepts
  them; headers are not forced into a dictionary. Error messages do not echo supplied
  secret values. This applies to whole-connection JSON; the separate field-level
  Headers JSON path remains tracked for the shared field-dialog audit.
- Twenty-one regression cases cover category repair/reset, exact values and history,
  thirteen comparisons with real native resource registration, and actual connection
  dialogs retaining invalid drafts before Cancel or correction. Neither validation
  path instantiates an exporter. The broader selection passed 752 tests separately
  on .NET 8 and .NET 10, with no failed or skipped tests; Release publish succeeded.
- Browser acceptance verified Category's linked Validation record, repair, clearing
  and Undo, rejected zero output capacity, semantic JSON error retention, Cancel
  preserving Redo, and both the dialog and Inspector at 800 by 720 pixels. Actual
  save/reopen retained Trial.Category, capacity 64 and the existing Logger reference.
  Comparing the downloaded project confirmed that only those two intended fields
  changed; every connection, encoded header and other project value remained intact.
- The updated preview remains open on port 5512 with its viewport override reset.
  Existing tabs, Core, WFENG, configuration contracts, runtime behavior and original
  telemetry were preserved. Application-wide harmonization remains in progress.

### Collection JSON validation and native-value preservation

- The shared JSON dialog now accepts a native JSON value validator, including an
  explicit clear operation. String-list and string-map fields apply their existing
  entry validation before closing the dialog and again before committing. Invalid
  shapes or values stay open for correction or Cancel. Whole-connection object
  validation retains its existing behavior through the same callback.
- OTLP owns its native encoded-header validation in the Connections/Logging slice.
  Field-level and whole-connection JSON share this validator. Valid repeated custom
  headers are retained as strings; unsupported visual-map shapes are not flattened.
  Error messages do not echo supplied secret values.
- Fifteen regression cases exercise OTLP headers, HTTP connection headers and HTTP
  trigger methods through the actual shared field dialogs. They cover invalid
  shapes and entries, correction, Cancel, no-op, clear, stale results, key spelling,
  unrelated settings, Undo/Redo and save/reopen. The focused selection passed 33
  tests and the broader selection passed 767 on each of .NET 8 and .NET 10, with
  no failed or skipped tests in the saved reports. Release publication succeeded.
- Browser acceptance verified invalid field-level header JSON remaining open,
  Cancel retaining saved values without adding history, native repeated headers,
  Undo/Redo and an 800-by-720 dialog. Actual save/reopen retained the exact encoded
  string and both repeated entries. Comparing the downloaded project confirmed
  that only the intended Headers value changed; all other project values remained
  intact. No workflow was run and no telemetry was sent to a collector.
- The updated preview is available on port 5513. Temporary viewport overrides were
  reset and existing tabs preserved. Core, WFENG and runtime behavior were unchanged.
  The application-wide inventory remains open.

### MQTT component editor audit and empty subscriptions

- Inspected all three component configuration registrations and descriptions.
  Connection Controller and Publisher have two ordinary settings, Connection and
  OutputCapacity; both already use shared typed fields. Publisher's feature-local
  documentation describes its publish-request input. Subscriber now reuses
  OptionPropertyRows instead of duplicating the shared row renderer, while keeping
  topic conversion and item editing within its MQTT slice.
- Native registration accepts null and an empty object as no initial subscriptions.
  The visual Topics group now displays them as empty without changing their saved
  representation on render or Cancel. An explicit last-topic removal omits Topics
  instead of writing an empty array, which the configuration provider can expose as
  an empty topic and the native registration rejects. Existing saved arrays are not
  silently migrated. Undo restores the original value and property spelling.
- Five new rendered-editor cases verify native registration without creating an
  MQTT client, null/empty-object preservation, mode-specific Add defaults, Cancel,
  exact key spelling, unrelated settings, last-topic removal, Undo/Redo and
  save/reopen. Existing ACK wiring/presentation tests remain in the broader selection.
  The selection passed 783 tests on each of .NET 8 and .NET 10, with zero failures
  or skipped tests in both TRX reports. Release publication succeeded.
- Browser acceptance used a temporary three-component project. It verified empty
  topics, Manual's QoS 1 default, Cancel without a history entry, Add/Remove and
  Undo/Redo, Automatic/Manual visibility with the saved pending limit retained,
  narrow scrolling and the outlined topic dialog. Publisher's connection selector
  retained a missing reference and excluded an HTTP connection; Controller showed
  its existing MQTT selection. No broker was contacted and no workflow was run.
- Actual save/reopen retained the empty Topics group and pending limit. Comparing
  configuration confirmed that only the explicit Topics removal changed; resources,
  unknown settings, capacities and missing references were preserved. The preview
  remains available on port 5514 with viewport overrides reset. Core, WFENG and
  runtime behavior were unchanged. Final MQTT validation and the remaining
  application-wide inventory are still open.

### MQTT topic validation and field JSON acceptance

- Topics reuse the existing authoring registration validator for supported native
  shapes, wildcard syntax and Manual-mode QoS compatibility. A minimal validation
  document isolates the field from unrelated missing connection references; no
  component factory or MQTT client is activated. Diagnostic paths use the actual
  selected workflow/component address.
- Saved invalid subscriptions remain visible and repairable. The Topics group uses
  the shared diagnostic link to Validation, recomputed on mode/selection changes.
  The item dialog rejects incompatible QoS before committing. Optional Topics JSON
  uses the shared field dialog and retains invalid drafts for correction or Cancel.
  Duplicate subscriptions, key casing and native string/number representations are
  preserved; this is not a configuration migration or a whole-component JSON editor.
- Eight rendered-editor cases cover invalid JSON and semantic values, correction,
  Cancel, no-op, clear, stale results, mode switching, linked diagnostics, Undo/Redo
  and save/reopen. The broader selection passed 791 tests on each of .NET 8 and
  .NET 10, with no failures or skipped tests. Release publication succeeded.
- Browser acceptance verified the component-specific validation path, linking to
  the Validation record, invalid draft retention, Cancel without history changes,
  repair and Undo/Redo. At 800 by 720 pixels the Topics group remains scrollable
  and the JSON dialog retains its Save/Cancel actions. Actual save/reopen retained
  both valid duplicate entries and cleared the old diagnostic. Comparing downloaded
  configuration proved that only the intended Topics repair changed; unrelated
  resources, settings and missing references remained intact.
- The updated preview is available on port 5515 with viewport overrides reset.
  No workflow was run or broker contacted. Existing tabs, Core, WFENG, runtime
  behavior and original telemetry were preserved. The remaining inventory and
  generic whole-component Advanced JSON work are still open.

### Shared whole-component JSON fallback

- Every registered component now has exactly one whole-component Advanced JSON
  action. NATS, Serial frame, RabbitMQ and BLE configurators retain their existing
  complete editors through an explicit capability. The other Inspectors reuse
  ComponentJsonEditor and the existing TextEditorDialog. Topics, Routes and QoS
  field JSON actions have specific labels so their scope is clear.
- The fallback requires an object, retains Type, rejects duplicate configuration
  keys through the shared parser and runs existing authoring registration checks.
  It preserves unknown properties, exact integers, bytes and missing references.
  Resource resolution, expression compilation, external files and unavailable
  host registrations retain the existing authoring-validator limitations. No
  factories, broker connections or workflows are activated by editing.
- Nineteen rendered tests cover all registered Inspector actions plus Logger and
  MQTT Publisher/Subscriber invalid drafts, Type changes, duplicate keys, Cancel,
  no-op, changed revisions, selection, disposal, exact values, credentials,
  Undo/Redo and save/reopen. The broader selection passed 810 tests on each of
  .NET 8 and .NET 10. Release publication succeeded. An older navigation test's
  positional last-button lookup was replaced by named View trace/Show related
  logs actions after an async stack identified it waiting on the new JSON dialog.
  Its original navigation and retention assertions remain intact.
- Browser acceptance verified invalid capacity retention, Cancel without history,
  visual refresh after Save, Undo/Redo and a no-op Subscriber Save. The 800-by-720
  dialog retains its actions. Actual save/reopen and downloaded-file comparison
  preserved the exact 9007199254740993 integer, Base64 string, duplicate topic
  entries, key spelling, missing reference and unrelated configuration. Only the
  intended Publisher capacity and test extension object changed.
- The browser-native ArgumentOutOfRange message follow-up is resolved by retaining
  framework resource strings, as recorded below. Original engine logs are unchanged.
- Preview port 5516 remains open with viewport overrides reset. Core, WFENG,
  runtime behavior and original telemetry remain unchanged. The remaining family
  reviews and final application-wide acceptance are open.

### Readable validation and shared component JSON actions

- The WebAssembly SDK defaults to resource-key-only framework exception messages.
  The browser app now sets UseSystemResourceKeys=false so existing native validation
  presents readable sentences. No exception-message rewriting or logger wrapper was
  added. The published runtime configuration confirms the setting is false.
- ComponentJsonAction supplies one flat Advanced JSON row for the generic fallback
  and the NATS, Serial frame, RabbitMQ and BLE editors. The feature-specific dialogs,
  cancellation guards and commit handlers remain in their existing slices.
- The saved regression reports contain 810 passing tests on each of .NET 8 and
  .NET 10, with no failures or skipped tests. Release publication succeeded.
- Browser acceptance on port 5517 imported the previous saved temporary project.
  Invalid Publisher capacity now displays a readable native sentence and leaves
  the draft open. Cancel retained capacity 96 and the missing connection reference
  without creating history. Reopening retained the exact extended JSON values.
  RabbitMQ's shared action opened its existing complete JSON dialog; Cancel followed
  by Undo removed only the temporary node addition. At 800 by 720 pixels the shared
  dialog retains its editor and Save/Cancel actions. The viewport was reset afterward.
- The updated preview remains open. No workflow was run or broker contacted; user
  tabs and saved project files were preserved. Core, WFENG, runtime behavior and
  original telemetry were not changed. The remaining inventory is still open.

### Scheduler syntax and dependent validation

- Scheduler metadata now supplies the existing positive-interval bound and uses
  Schedules.Cron for five-/six-field syntax validation. No schedule parser or
  runtime behavior was duplicated. Authoring errors explain the expected fields
  instead of exposing the parser end-of-input control character.
- Include seconds is available before a cron expression is entered, directly after
  Cron. Changing it revalidates the expression without rewriting it. Saved timezone
  identifiers remain editable and are never replaced with browser-local choices.
  Timezone availability is host-dependent; browser validation cannot certify a
  different runner timezone database. Existing native registration remains authoritative.
- ValidatedTextField now rechecks dependent constraints against its current draft
  on parameter updates. It does not discard or auto-commit an invalid draft when
  another property changes. Actual saved-value changes still refresh the editor.
- Eleven regression cases cover native cron syntax, seconds modes, whitespace,
  clearing, runner-specific zone preservation, dependent unsaved drafts, mixed-case
  keys, unknown properties, history and save/reopen. The broader selection passed
  821 tests on each of .NET 8 and .NET 10, with no failures or skipped tests.
  Release publication succeeded.
- Browser acceptance verified invalid drafts without history changes, zero-interval
  rejection, seconds-mode diagnostics linked to Validation, correction, Undo/Redo
  and the readable final message. Save/reopen retained the six-field expression,
  seconds option, timezone, key spelling and unknown field. Comparing downloaded
  configuration confirmed only the intended cron and seconds edits changed.
- The 800-by-720 Inspector remains scrollable with aligned label/value cells.
  The viewport was reset and the updated preview remains open on port 5518.
  No workflow ran. User tabs and saved projects were preserved. Core, WFENG,
  runtime behavior and original telemetry were unchanged. The remaining inventory
  and application-wide acceptance are still open.

### Visual Source values

- JsonSourceDialog now offers a typed visual array list alongside whole-array JSON
  and runner file modes. JsonArrayValueEditor reuses the Property Grid and compact
  actions; JsonValueEntryDialog uses standard outlined fields for string, number,
  boolean and null values, with JSON for arbitrary object/array items.
- Item changes remain local to the outer draft. Parent Save makes one history edit;
  parent Cancel discards every item change. Item Cancel and unchanged edits preserve
  the original source text. Numeric tokens do not pass through floating point;
  duplicate object members and mixed payload types remain intact.
- Invalid JSON cannot switch to Visual or save. File and inline drafts survive
  switching. Existing file references are not opened. Explicit invalid inline values
  remain repairable JSON rather than being silently replaced with an empty array.
- Twenty-one new regression cases cover type fidelity, invalid values, item and
  parent cancellation, no-op, stale results, Add/Edit/Remove, exact property names,
  unknown fields, Undo/Redo and save/reopen. The broader selection passed 842 tests
  on each of .NET 8 and .NET 10, with no failures or skipped tests.
- Browser acceptance found and fixed the JSON editor collapsing after a mode
  switch. A stable dialog size and feature-local editor height now support both
  modes. Shared value/action alignment keeps buttons beside the preview. Release
  publication succeeded, and an uncached preview verified the final scoped styles.
- Browser checks covered invalid numbers, outer Cancel without history, Visual/JSON
  switching, invalid JSON retention and repair, switching through File mode, narrow
  layout, Undo/Redo and actual save/reopen. Downloaded configuration comparison
  confirmed only the intended first value changed; 1e400, duplicate object members,
  other values, mixed-case property spelling and the unrelated Extra setting remain.
- The final preview is open on port 5520 with viewport overrides reset. Existing
  user tabs and saved project files were preserved. No workflow ran. Core, WFENG,
  runtime behavior and original telemetry are unchanged. Other inventory rows and
  application-wide acceptance remain open.

### Retry timing and Data Generator locale

- Retry now supplies the existing registration bounds to shared duration and
  integer fields: positive attempt timeout up to the timer limit, explicit
  infinite timeout, one or more retries, and zero-to-one-day delay/cap.
  Installed Polly validation confirms these policy bounds. An older numeric
  test incorrectly expected zero retries; it now matches the native contract.
- Data Generator Locale uses the same Bogus resource lookup as the runner.
  Unknown names are diagnosed without rewriting saved values. Clearing the
  field removes the override and retains the existing default behavior.
- No new control or runtime behavior was introduced. Feature metadata stays
  in its existing definition slice and uses the shared flat Property Grid.
- Twenty added regression cases cover policy compatibility, locale identifiers,
  duration limits, infinite timeout, invalid saved values, explicit repair,
  clearing, exact key spelling, unrelated fields, Undo/Redo and save/reopen.
  The broader selection passed 862 tests on each of .NET 8 and .NET 10.
  A final duration check passed 17 tests on each framework. None were skipped.
  Release publication succeeded.
- Browser checks verified linked Validation messages, invalid drafts without
  history changes, infinite timeout, rejected excessive delay, zero delay,
  clearing the cap, locale correction, Undo/Redo and narrow scrollable layout.
  Actual save/reopen restored the settings. Comparing downloaded configuration
  confirmed only the intended timeout, delay, cap removal and locale changed.
- The updated temporary-project preview remains open on port 5521 with normal
  viewport restored. Existing tabs and project files were preserved. No workflow
  ran; runtime, Core, WFENG and original telemetry are unchanged. Remaining
  inventory entries and application-wide acceptance are still open.

### Filtering component acceptance

- Change Filter and Deduplicate were checked against their native registration
  and runtime options. Deduplicate was missing the positive Retention constraint
  in its metadata. The existing shared duration field now receives a one-tick
  lower bound; no timer upper bound is introduced because retention uses elapsed
  timestamps rather than a timer. No runtime or comparison behavior changed.
- Thirteen added regression cases cover missing/invalid/long saved retention,
  explicit repair, TimeSpan boundaries, required engine input, custom runner
  engines, comparison toggles, integer limits, expression Save/Cancel/no-op,
  exact source and property spelling, unrelated fields, Undo/Redo and save/reopen.
  Existing expression tests were extended rather than duplicating their harness.
- The broader selection passed 869 tests on each of .NET 8 and .NET 10. The final
  extended source-editor selection passed 15 tests on each framework, including
  six new filtering cases. No tests were skipped. Release publication succeeded.
- Browser checks verified saved-error navigation to Validation, invalid drafts
  without history, long retention repair, Undo/Redo, comparison and capacity edits,
  custom engine names and cancelling an edited key-selector draft. Narrow rows
  remained aligned and scrollable. Save/reopen restored both components.
- Comparing the downloaded configuration confirmed only the intended settings
  changed; both original expressions, unknown values and saved key spellings
  remained intact. No workflow ran and no runtime capability is claimed for the
  synthetic custom engine. Core, WFENG and original telemetry were unchanged.
- The temporary-project preview remains open on port 5522 with normal viewport
  restored. User tabs and saved projects were preserved. Other inventory rows
  and application-wide acceptance remain incomplete.

### Typed consumer references

- RabbitMQ Acknowledge previously used an unrestricted text field for Consumer,
  although its native contract requires an amqp.consumer in the same workflow.
  RelatedComponent metadata now selects a shared flat ComponentSelect. It reuses
  the standard select control and current designer graph; no runtime lookup or
  alternate reference format was introduced.
- The selector offers only compatible current-workflow nodes, retains missing
  saved references as disabled choices, and rechecks a selected target before
  committing. It never repairs references or inserts defaults while rendering.
- Five regression cases cover omitted, valid, missing, incompatible and other-
  workflow references, no-op selection, removed targets, exact property spelling,
  unrelated fields, Undo/Redo and save/reopen. The broader selection, including
  existing RabbitMQ declaration and feedback tests, passed 888 tests on each of
  .NET 8 and .NET 10, with no failures or skipped tests. Publication succeeded.
- Browser acceptance verified filtered choices, Escape without history changes,
  keyboard selection, Undo restoring the missing reference, Redo, narrow layout
  and actual save/reopen. Downloaded configuration comparison confirmed that only
  the intended Consumer value changed. The preview remains open on port 5523.
- Publisher and Consumer already have inline declaration groups. Their final
  validation/acceptance and visual typed-argument coverage remain open rather
  than treating passing existing tests as full editor completeness. Core, WFENG,
  runtime behavior, telemetry, user projects and tabs were not changed.

### Visual RabbitMQ declaration arguments

- Exchange, queue and binding declarations now expose an Arguments group with
  Add/Edit/Remove. The shared RabbitMQ editor uses the standard dialog and outlined
  form controls; ordinary declaration fields remain flat Property Grid cells.
- Typed scalar values, Base64 bytes, exact Int64/decimal text, nested arrays and
  tables can be edited visually. Detached drafts preserve untouched field spelling
  and JSON values. The existing RabbitMQ configuration reader validates saved
  arguments; no new runtime contract or broker operation was introduced.
- Missing/invalid shapes remain available through Advanced JSON. Duplicate names
  are rejected using the configuration provider's case-insensitive semantics.
  Cancel, unchanged Save and stale selection results do not create history entries.
- Added 36 tests covering supported types, invalid values, nested preservation,
  duplicate names, Add/Edit/Remove, cancellation, stale results, Undo/Redo and
  save/reopen. The broader selection passed 924 tests on each of .NET 8 and .NET 10.
  A final focused run after improving error context passed 36 tests per framework.
- Browser acceptance in a temporary project verified numeric rejection, Cancel,
  unchanged Save retaining Redo, nested duplicate validation, byte editing,
  keyboard type selection, Add/Remove history, narrow layout and save/reopen.
  Download comparison confirmed only the intended TTL and byte value changed;
  the original maximum Int64 value and unrelated configuration were preserved.
- Updated preview: port 5524. Core, WFENG, runtime behavior and telemetry remain
  unchanged. Application-wide harmonization and other inventory gaps remain open.

### RabbitMQ configuration-editor acceptance

- Publisher and Acknowledge now use Consumer's short Buffer size label, and
  Publisher also uses Max bytes and Timeout (s). Serialization
  names and native defaults have not changed. Publisher/Consumer reuse shared
  OptionPropertyRows instead of duplicating their row markup.
- Inline declaration names and routing keys use the shared flat validated field,
  with existing RabbitTopology validation and linked diagnostics. Whitespace-only
  routing keys remain exact values. Unknown exchange types remain visible until
  explicitly repaired. The native 256-declaration limit prevents new additions
  while preserving saved excess declarations for repair/removal or JSON editing.
- Unchanged declaration defaults and stored Boolean strings are not materialized
  or rewritten. Formatting/order-only component JSON saves retain original records
  and Redo. Consumer queue validation reuses RabbitConsumerOptions.Validate.
- Eighteen additional regression cases cover all RabbitMQ numeric properties,
  UTF-8 limits, preserved routing keys/defaults, unknown types, declaration limits,
  optional JSON no-op saves, saved fields, feedback references and history. The
  broader suite passed 942 tests on each of .NET 8 and .NET 10, without skips.
- Browser acceptance verified all three Inspectors, invalid drafts, validation
  navigation, unsupported-type repair, Requeue history, optional JSON no-op save,
  narrow layout and actual save/reopen. Download comparison found only the nine
  intended edits; existing arguments, exact Int64 data and whitespace routing keys
  remained intact. This was an editor trial, not a broker execution trial.
- A proven shared layout issue was fixed: long property help now scrolls within a
  bounded area instead of crowding out the settings. Keyboard scrolling and the
  normal viewport were verified in the published build. Updated preview: port 5526.
- Core, WFENG, runtime behavior, telemetry and user projects/tabs were unchanged.
  Other component families, connections and topology/service acceptance remain open.

## NATS subject validation and settlement selection

- NATS Subscriber subject/queue group and JetStream stream/consumer names and
  provisioning subjects now use the existing runtime validators through the
  shared flat text editor. Invalid drafts do not replace saved configuration.
  Clearing optional groups/filters removes the override; clearing Stream subject
  restores its existing default. Hidden provisioning values remain untouched.
- JetStream Settlement reuses the shared component selector. Choices are limited
  to JetStream Consumers in the current workflow; missing or incompatible saved
  references remain visible. The selector rejects removed targets before commit.
- Shortened size labels to Buffer size, Max bytes and Stream bytes. Configuration
  keys, defaults, limits and runtime behavior are unchanged.
- Seventeen additional regression cases cover native text constraints, optional
  resets, key spelling, unknown fields, no-op Redo preservation, reference
  filtering, stale choices, Undo/Redo and save/reopen. The broader shared-editor,
  metadata and NATS project suite passed 855 tests on each of .NET 8 and .NET 10.
  Release publish passed; existing dependency and compiler warnings remain.
- A temporary browser project verified linked subject diagnostics, invalid-draft
  rejection, reference repair and Undo/Redo, optional group removal, provisioning
  toggles, exact Int64 retention, keyboard selection and narrow layout. Actual
  save/reopen preserved exactly the four intended configuration changes, with no
  other changes to resources, unknown values or references. Updated preview: 5528.
- No broker was contacted. Final whole-family numeric/reference acceptance,
  other component families and connection/topology/service reviews remain open.

## NATS family configuration-editor acceptance

- Finished the seven NATS/JetStream editors' numeric and reference review against
  existing native configuration contracts. Settings and payload contracts remain
  unchanged; per-message publication/request/reply data is not duplicated as fixed
  configuration. Authoring documentation now points to the docked Property Grid.
- Shared select editors now include an unlisted saved value as a disabled entry
  in the opened list. The closed control already displayed its original text and
  continues to do so. Different enum casing, numeric representation and future
  values are not normalized or labelled invalid merely for being unlisted.
- Added 68 regression cases: 24 numeric-bound cases across all seven types,
  18 typed connection cases, 21 actual Visual/JSON dialog Save/Cancel/no-op cases
  and five saved-choice preservation cases. Dialog tests reject malformed JSON and
  invalid capacities while retaining original settings, exact Int64 text, binary
  text, unknown fields and Redo. Broader shared-editor/metadata/NATS tests passed
  923 cases on each of .NET 8 and .NET 10. Release publish succeeded with existing
  dependency/compiler warnings.
- Browser acceptance covered all seven Inspectors in a temporary project:
  filtered/missing connections, numeric validation, saved enum repair with history,
  dialog Cancel, no-op Save with Redo, hidden provisioning fields, narrow dropdowns
  and save/reopen. Download comparison verified exactly nine intended edits;
  enum casing, exact retention text, resources and the other workflow were unchanged.
  The normal viewport was restored and the updated preview is on port 5530.
- This completes NATS configuration-editor acceptance, not broker-runtime or NATS
  connection-resource acceptance. No broker was contacted. Core, WFENG, runtime,
  telemetry and user projects were not modified. Remaining component families,
  connection definitions and environment/host/service acceptance are still open.

### Serial configuration editors

The port selector follows the existing workflow-scope restriction. A saved
reference outside the current workflow stays visible until explicitly replaced.
Studio checks line and frame settings with the native option validators without
opening a port. The component editor does not check hardware availability or
exclusive port ownership.

The two-workflow browser trial changed a reader’s port, a writer’s framing and
a text node’s line ending. Undo restored the incompatible port; Cancel retained
LF after an invalid JSON edit. The downloaded and reopened configuration contained
only those three intended changes, including at 800 px viewport width with no
horizontal page overflow. Both .NET 8 and .NET 10 passed 944 selected Studio tests.

### ROS configuration editors

The docked resource Inspector edits the native node name and namespace. Node
references use the existing typed selector and can target a shared or workflow
resource. The component JSON editor and runtime registration now create the same
managed publisher/subscription options. No ROS context, generated message type
or native middleware is required to edit these settings.

Regression tests bind the profile before and after a visual edit using the runtime
binder. Editing durability on an empty profile retains the effective depth and
reliability. Advanced JSON rejects invalid durations and unsupported queue modes.
The published browser build preserves constructor metadata for the QoS record.
The two-component browser trial verified node creation, narrow layout, Undo,
Redo, Cancel, and exactly four changes after save/reopen. On both .NET 8 and 10
the selected Studio tests (968) and ROS component tests (20) passed.

### Message expressions and file-backed settings

The shared Input panel previews either a value or an error via the existing host
evaluator and native message envelope reader. An ordinary object with the same
fields as a failure remains a value. Sample type, drafts, and results are never
written to the project. Source and Reducer validate their remaining settings and
links without accessing external initial JSON files.

The browser trial switched between transient and permanent errors, cancelled
without creating history, saved a predicate and exercised Undo/Redo. A comparison
of the downloaded configuration found exactly the edited Retry expression,
Mapper engine and Router predicate; the linked route, unknown fields, file
references and numeric spelling were retained. Reopening and the sample selector
at 800 px were also checked.

### HTTP Trigger and MQTT Connection Controller

HTTP Trigger's shared settings reader validates both the Studio draft and the
host registration. Only the host parses the ASP.NET route and maps the endpoint.
Reply timeout and pending limit apply to Reply; shutdown policy applies to both
modes. Changing mode preserves inactive values and wiring. A saved Response link
in Acknowledge mode remains visible to validation instead of being silently removed.

In a temporary project the published editor rejected zero timeout and an unknown
shutdown mode, retained a numeric response mode across Undo, and reopened with
the edited timeout and custom method. The same trial repaired a missing MQTT
reference, rejected zero capacity and restored the original numeric spelling on
Undo. Only the four intended settings differed in the saved configuration. Both
Inspectors were checked at 800 px. On each target framework, 1,015 selected Studio
tests and all 56 ASP.NET Core hosting tests passed.

### BLE Client and Notifications

Device address and UUIDs can be entered offline. Numeric enum settings display
their names without rewriting saved values. A malformed write value stays in
the cell as a draft; neither editing it nor attempting a lossless conversion
replaces the stored bytes. Read preview uses the fixed target and timeout only;
command mode does not preview a characteristic retained from fixed mode.

Both components use the shared whole-component JSON editor. Tests exercise
invalid drafts, no-op Save, Cancel, stale results, selection changes, disposal,
Undo/Redo and save/reopen. In the browser, invalid field drafts left the saved
UUID and bytes intact. The trial repaired a missing notification connection,
undid and redid that repair, and switched the client to command mode and back.
The reopened project retained the original UUID spelling, Base64 data, numeric
encoding and timeout spelling. The 800 px layout had no horizontal page overflow.
On each of .NET 8 and .NET 10, 1,011 selected Studio tests passed. Device I/O
was not part of this configuration-editor trial.

### GATT Server hierarchy

The service editor accepts the same string spellings for advertising booleans and
output capacity that the configuration binder accepts, without rewriting them.
Editing legacy UTF-8 text keeps the original Value field. Changing a descriptor's
data keeps the saved encoding representation. Adding and then removing a service
in the visual draft no longer converts a legacy project to Services.

The published browser trial opened two services, rejected an invalid descriptor,
switched between Visual and JSON, cancelled, then saved the corrected edit.
Undo restored the original descriptor and Redo restored the new bytes. A second
node exercised legacy text editing. After save/reopen, a comparison of the full
configuration found exactly the two intended value changes. The 640 px viewport
and dialog had no horizontal overflow. All 27 targeted server-editor and grid
tests passed on each of .NET 8 and .NET 10. This trial did not start advertising.
