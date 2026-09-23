# Preparing removal of activation and instance wrappers

This step transfers component-specific lifetime out of ComponentActivationContext
and ComponentInstance. It does not claim that either class has been deleted yet.

## Completed

- Removed SetLifetime, its stored task/callback and ComponentInstance's completion
  and disposeAsync constructor/Create arguments. Component work and internally owned
  resources now use the existing IFlowNode Completion/DisposeAsync contract.
- No new node wrapper, manager, callback registry or lifetime interface was added.
  Application/revision resource scopes and borrowed host dependencies are unchanged.
- Revision completion observes registered native input completions alongside the
  node's complete work. A native input fault reaches siblings immediately while
  the application still waits for remaining accepted work and preserves the first
  failure. An error in additional node work is also covered by the node's completion.
- Consolidated node binding and event attachment under one cleanup boundary.
  Failed declaration construction disposes the returned node; failed attachment
  disposes the initialized instance and any event bridges already attached.
- Removed ComponentInstance.InitializeAsync and its dependency on activation context.
  Removed ValidatePorts, which compared generated metadata against ports created
  from the same declarations. Duplicate/null/name/reserved-policy validation and
  named event/output collision checks remain in declaration/port construction.

## Migration

This is a preview API change. Move resources previously passed through SetLifetime
to their actual owner: the node for component-internal resources, or the existing
application/revision resource scope for shared resources. Include node-owned work
in Completion, and release node-owned resources in DisposeAsync, preserving all
cleanup failures. Composition no longer replaces these contracts with unrelated
tasks or cleanup callbacks. Legacy observational event forwarding still has its
own completion in ComponentInstance; this has not been mislabeled as node work.

Tests formerly supplying instance completion overrides now use nodes that own
their asynchronous work. Their native input can fault independently; tests verify
early sibling faulting, delayed final completion, first-error identity and disposal.
Cleanup tests verify node-owned cleanup, multiple reported failures and event unlink
failures. No test-only replacement activation or lifecycle engine was introduced.

## Validation

- Release solution build: zero warnings/errors.
- Full suite: 2,526 passed, zero failed, 29 TRX reports across configured targets.
- Focused binding, cleanup and revision lifetime cases passed.
- API baseline updated; regression checks no SetLifetime or constructor overrides.
- Core package-only consumer passed on net8/net10, including revision rejection
  followed by continued delivery through the active revision.
- Evidence: artifacts/component-lifetime-ownership/ (build.log, api.log,
  focused.log, tests.log, tests/, package.log, core-consumer/, verification.json).

## Remaining

Configuration binding/processing settings, named port declarations and event
composition still depend on the two classes. The next transfer must move revision
construction to the existing DI factory/port-selector model, then remove the
remaining instance-based port/compiler consumers. The classes still exist; the
requested overall deletion is not complete. Changes are local and uncommitted.
