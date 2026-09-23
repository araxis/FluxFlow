# Refactoring closure review

## Follow-up: revision resource callback simplification

Replaced IApplicationResourceRegistrar with RevisionResourceRegistration, a standard
delegate. Removed its RegistrationIdentity property and the registrar DI extension
class. Resource contracts carry callbacks rather than impersonating registrars.
Native AddSingleton registers callbacks; a local HashSet using delegate equality
deduplicates identical target/method callbacks across host and embedded contracts.
No provider, cache, manager or ownership wrapper was added.

ApplicationResourceRegistrationContext remains intentionally: it provides immutable
definition, sequence/ID, borrowed host and candidate service collection. Ordinary
ResourceRegistration has a different per-resource input and registration timing.
This change simplifies the revision contract without pretending those capabilities
are identical or removing candidate rollback. The two component-registration inputs
remain a separate review item. No additional component setup was started.

Validation: solution build zero warnings/errors; 2,646 solution test executions pass.
Existing hot-reload, rollback, closure-retirement and exact resource-disposal tests pass.
The host/embedded test now also registers two distinct delegate objects for the same
method; it still runs once. New tests cover original revision context, ordered distinct
callbacks and cancellation between registrations. API baseline records the intentional
preview contract replacement. Evidence: resource-delegates-*.log in cleanup artifacts.

## Follow-up: shared engine registration

The Mapper/conditional-link registration gap described below is now fixed.
Both bundled engines register IFlowExpressionEngine directly; Mapper resolves that
same contract and compiles FlowValue, while links compile bool. The separate
Configuration.ExpressionCompiler delegate is removed. Engine packages reference Core
only. This adds real typed engine implementations, rather than a second adapter catalog.
Static mapping convenience APIs call those implementations too.

ForMessage exposes original message and input/payload plus flat value/headers/IDs/time.
Bound parameter override semantics are preserved. C# compiles the expression tree once
and evaluates only used arguments, without per-message reflection Invoke. JSONata uses
a fresh environment/DOM per evaluation and reuses the input token for payload aliases.
JSON bridging still has allocation costs; no throughput improvement is claimed.

New tests exercise ordinary configuration from Mapper through a conditional link for
both languages, workflow engine override, bound parameters, payload aliases and strict
boolean results. All 2,642 solution test executions pass; solution build has zero
warnings/errors. Public API baseline reflects removed ExpressionCompiler and public
FlowMapContext.ForMessage. Evidence: shared-expressions-*.log in the cleanup artifacts.
After removing the Configuration project dependency, the release-plan assertion was
updated for two dependency waves instead of three. All 111 release checks then passed
again against that final project graph (shared-expressions-release.log).

Remaining work below is not declared complete. Mapper files still resolve in setup;
link expressions remain text as before. File resolution for link expressions, the two
registration inputs and revision resource registration require further bounded review.
No new component setup was added. The latest user ordering is all cleanup before setup,
superseding the earlier order that allowed Router before revision-resource review.

## Follow-up: first expression cleanup slice

IFlowCompiledExpression<T> and EvaluatingCompiledExpression<T> are removed.
IFlowExpressionEngine.Compile<T> now returns Func<FlowMapContext,T>. Condition parameter
binding uses a closure instead of the private BoundCondition class. This removes a
result abstraction without changing expression-engine selection, parameters, evaluation
or cancellation semantics. Empty-source validation in the default compiler is preserved
and covered by three regression cases per target. The setup/legacy compiler integration
below is still unfinished; no Router setup was added.

Validation of this slice: clean solution build and 2,624 passing solution tests;
public API baseline updated for the intentional compiled-return-type change.
Evidence: artifacts/cleanup-review-20260920/expression-delegates-*.log.

Reviewed the current working tree on `codex/core-only-component-redesign`, based on
`6b85b45f72cf8ccaec27579e340799c1118aa3c2`. This is a code inspection before adding
Router configuration, not a declaration that every remaining abstraction can be removed.

## Verified complete within the agreed scope

- Ordinary composition resolves the host's singleton FlowApplication. It does not
  build another provider or register services during Compose. Component/port lookup
  uses keyed native registrations. Workflows hold actual nodes and route declarations,
  not another port lookup table.
- The removed component contexts, instances, topology/construction facades and port
  metadata files were not found in the production source file inventory.
- FlowApplication is a single file. Workflow-level DiscardedOutputs and its public
  ordinary/JSON/definition/revision declarations are absent from production source.
- Component projects have no IConfiguration/IServiceProvider references in their C#
  source. Setup remains outside component behavior.
- The production BuildServiceProvider search found the retained revision snapshot
  builder only. That provider supplies candidate isolation/rollback; it is not an
  accidentally restored ordinary composition path.

## Remaining integration work

### 1. Expression registration does not cover conditional links

Evidence: `src/FluxFlow.Configuration/ApplicationConfiguration.cs` applies document
links using Core `IFlowExpressionEngine`. `Configuration/Json/Links/ApplicationConditionCompiler.cs`
requires that interface and throws MissingConditionEngineException when it is absent.
The bundled `CSharpExpressions.AddCSharpExpressions` and
`JsonataExpressions.AddJsonataExpressions` register keyed `ExpressionCompiler`
delegates only. Mapper resolves those delegates in MapperRegistrationExtensions.

Consequently, enabling a bundled engine for Mapper does not by itself enable that
engine for conditional links. This is a real incomplete integration, although it was
previously acknowledged as a retained legacy contract. It should be addressed before
we claim one interchangeable expression setup across the framework.

The old contract includes synchronous typed results, workflow/application engine
selection, input/payload/message variables, bound link parameters and native predicates.
The newer contract takes FlowMessage plus cancellation and returns ValueTask<FlowValue>,
with separate value/header/ID parameters in expressions. A signature replacement alone
would lose behavior. Next slice: define and verify the shared predicate boundary,
including strict boolean results, engine scope, parameters, compile-time validation and
expression files. Do not solve this by synchronously blocking a ValueTask or adding
another catalog/manager.

### 2. Two setup registration contracts remain

`FluxFlow.Configuration.ComponentRegistration` takes IServiceCollection and a
configuration section. `FluxFlow.Core.Configuration.Registration.ComponentRegistration`
takes IServiceCollection, an address and a ComponentDefinition. Revision preparation
uses the latter; ordinary IConfiguration setup uses the former. Both ultimately register
the same ComponentFactory and native ports, so this is not a second component executor.

Their identical short name hides different inputs. Consolidation is a remaining setup
design question, not permission to remove the revision entry point. Preserve immutable
definition snapshots, embedded registration delegates and configurations from sources
other than IConfiguration. Do not add a third registration model for Router.

### 3. Revision resource registration is still a separate contract

ApplicationRuntimePreparation calls ApplicationSetup.RegisterResources with
ApplicationResourceRegistrationContext. It carries the definition, revision sequence/ID,
borrowed host services and candidate registrations. ApplicationResourceContract implements
IApplicationResourceRegistrar; registration identities deduplicate embedded/host callbacks.
Ordinary configuration instead uses the ResourceRegistration delegate.

This is not fully unified with ordinary resource setup. It also has real revision-specific
capabilities, so deleting the context or isolated provider would not be a safe cleanup.
A future migration must preserve embedded contracts, host dependency borrowing,
transactional candidate ownership, cancellation and failed-revision rollback. It does
not need to block a component-only setup slice once the expression boundary is settled.

## Router setup prerequisite discovered during review

RouterOptions.Routes currently contains Predicate<FlowValue>, and Router evaluates each
predicate against message.Value. It forwards the original message to every matching
output; its ordinary Output carries unmatched messages/errors.

The earlier suggestion that setup can supply expressions using headers/IDs without
touching Router was too broad: that context is absent from its callback. Decide the
required predicate input explicitly as part of the expression slice. Do not fabricate
a new envelope from the value, which would lose the real message identity and headers.

## Deliberate deferrals and necessary mechanisms

- Unconnected-output build validation, automatic application error collection and the
  older low-level output-drain policy remain deferred by the user.
- FlowWorkflow naming was discussed; no rename has been performed. It is naming work,
  not an execution defect or a prerequisite for expression integration.
- Lifecycle RunOnce publication, native-block identity checks, the weak component alias
  guard and the real isolated revision provider have demonstrated behavioral purposes.
  Their presence is not evidence of an unfinished removal.

## Order of work

1. Close the expression/predicate integration gap with focused evidence and preserved
   semantics, before layering Router setup over the split contracts.
2. Implement one complete Router configuration scenario using that boundary and the
   existing native registration path; keep any necessary callback change explicit.
3. Track revision registration/resource convergence as separate bounded work rather
   than claiming it complete or attempting a broad rewrite alongside Router.

No production behavior changed in this review. No new test run was needed for this
documentation-only increment. The preceding checkpoint's 2,618 passing tests and clean
Release build remain historical validation of that checkpoint, not proof that all
architectural integration work is complete.
