# Revision factory resolution uses keyed DI

The previous checkpoint was verified against the branch, HEAD and saved validation
results before editing. Ordinary construction remains on the single DI builder.
This step simplifies the still-existing revision activation boundary.

## Removed

- CreateFactoryMap and its host/embedded factory merge into two temporary dictionaries.
- The Configuration.Factories.ComponentFactory delegate and its source file. It only
  wrapped node creation plus context.BindAsync and shared a misleading short name with
  the ordinary application factory, despite having a different contract.
- Non-keyed KeyValuePair service registrations and scanning/enumerating them to build
  another lookup structure.

AddComponent now stores the original synchronous or asynchronous Func as a native
keyed singleton under the component type. Revision preparation resolves that Func
directly, invokes it, then binds the returned node. Embedded callbacks still belong
to the candidate's existing ApplicationSetup snapshot; this allows callback changes
between revisions without rebuilding the host provider. No replacement catalog,
manager, factory class or public activation helper was added.

## Preserved behavior

Registration trims type keys, uses ordinal lookup, remains lazy and accepts repeated
registration of the same delegate. Conflicting synchronous/asynchronous registrations
are rejected without modifying services. Before constructing candidate resources or
nodes, preparation checks embedded callbacks against host registrations. An identical
async callback is accepted; a conflicting callback rejects the candidate.

Binding, resource snapshots, event surfaces, draining, disposal and revision adoption
remain on their existing paths. Tests exercise successful embedded/host composition,
callback replacement, failed replacement, and preservation of active revision routes.
The conflict regression now covers both synchronous and asynchronous host factories.

This is a preview API break for callers of CreateFactoryMap or the removed delegate.
Normal AddComponent registration signatures remain unchanged. The ordinary
Application.ComponentFactory(IServiceProvider) contract is unchanged too.

## Validation

- Release solution build: zero warnings/errors, including benchmark compilation.
- Focused revision assembler suite: 25 passed.
- Full suite: 2,524 passed, zero failed, 29 TRX reports across configured targets.
- API baseline updated; regression verifies removed method/type remain absent.
- Fresh Core package-only consumer passed on net8 and net10. Added a public-API
  revision scenario that activates sync/async factories, rejects an unknown type,
  then verifies the previous revision still delivers its original payload.
- Link-compilation benchmark setup now invokes the existing internal binder outside
  the measured operation. Its workload is unchanged; no benchmark run or performance
  improvement claim is made. Only the benchmark assembly gained internal access.
- Evidence: artifacts/revision-keyed-factories/ (build.log, focused.log, api.log,
  tests.log, tests/, package.log, core-consumer/, verification.json).

## Remaining work

ComponentActivationContext and ComponentInstance are still present for revision
port binding, processing settings, event attachment and additional lifetime work.
Removing that boundary requires transferring those responsibilities first. This
change removes lookup machinery; it does not claim a fully unified revision authoring
model or completed context removal. No commit, push or publication was performed.
