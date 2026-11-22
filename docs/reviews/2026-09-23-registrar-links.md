# Component registrars own link registration

## Request and result

The user preferred Workflow Engine's local registration pattern: bind the component's
configuration, register its factory/ports, and register its links in that same method.
They also requested configuration-only Error routing from the existing Output.

ApplicationConfiguration now only traverses sections and invokes their registrars.
Its port-address discovery, port-property inference and separate link parsing are
removed. LinkRegistrationExtensions replaces the internal LinkRegistration helper,
with AddInputLinks, AddOutputLinks and AddErrorLinks accepting already bound
OneOrManyLinks values. No new builder, catalog, context, port model or runtime wrapper.

Scheduler, Gate, Source, Mapper, Router and Batch explicitly register their DTO links.
Router reads its known dynamic output properties in its own registrar. Unknown custom
properties are not guessed from native DI registrations. Scheduler retains explicit
rejection of Input because it has no input. Registrars choose property/port mappings;
the old central case-collision/ambiguous-port policy is removed along with discovery.
Bundled properties still bind case-insensitively; port addresses retain exact spelling.

## Error contract

- Error accepts the same address, object or mixed-list syntax as other link properties.
- It selects IsError envelopes from the existing Output; no physical Error address,
  component settings or runtime behavior is introduced.
- Optional conditions evaluate only after IsError and receive the original payload,
  headers and message context. An unconditional Error link needs no expression engine.
- Output retains its existing normal/error behavior. An error may go to both selections,
  including twice to the same destination if both Output and Error select it.
- The private keyed-DI link identity includes the error filter. Equal Error declarations
  collapse; Error and ordinary Output remain different selections.
- No automatic error collector, retry, logging or error-to-exception conversion is added.

## Preserved behavior and migration

Input-side and output-side declarations still share one canonical keyed LinkFactory;
conditions, explicit/default engine resolution, arrays, Events, named ports, relative
and full addresses retain their existing path. Registration remains transactional.
Build resolves native ports, validates routing and compiles conditions before Start.
LinkConditionCompiler is registered lazily as an internal native singleton; a provider
reuses compiled engine/text pairs and compilation failures. There is no session key
or secondary provider. Component construction stays deferred.

Custom registrars must call the new link helpers explicitly. Registering a native block
alone no longer scans configuration. Examples and the isolated package consumer were
migrated; test-only custom registrars now register their own bound links too. Three
tests of the removed global ambiguity policy were removed. A new test proves that a
ReceiveFrom property can explicitly map to an Inbound port without central discovery.

No Core or component production files were changed for this step. Existing working
tree changes in those projects belong to earlier approved work.

## Validation

- Configuration: 280 passed on each of .NET 8 and .NET 10. Coverage includes all six
  Error adapters, casing, deduplication, snapshots, unchanged Output behavior, original
  envelopes, C#/JSONata conditions, skipped evaluation for normal values, and Add/Build
  failure timing. Existing input/output, events, engine and transaction regressions pass.
- Full Release solution: 1,994 passed, no failures or skips.
- Public API baseline accepted and verified: three link registration helpers and six
  Error configuration properties.
- Isolated NuGet consumer: .NET 8.0.31 and .NET 10.0.12 both passed. It declares one
  link from both ends and checks seven unique delegates for the complete flow.
- Evidence: artifacts/pre-setup-review-20260923/registrar-links-{second,full,api,package}.log.

No commit, merge or publication performed.
