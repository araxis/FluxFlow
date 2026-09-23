# Post-revision cleanup

The review followed removal of hot reload from minimum Core. It preserves ordinary
configuration registration, native keyed DI construction, links and application lifetime.

## Changes

- Removed IApplicationDefinitionSource, StaticApplicationDefinitionSource and
  ConfigurationApplicationDefinitionSource. These reload-era wrappers no longer have
  a runtime consumer. Configuration reading directly returns ApplicationDefinition.
- Moved ConfigurationJsonReader and its regression tests into FluxFlow.Configuration.
  ApplicationDefinitionConfiguration.Read is synchronous. Core no longer parses
  IConfiguration or explicitly references Configuration.Abstractions. This does not
  claim that no transitive dependency references that assembly.
- AddFluxFlowApplicationAsync retains its ValueTask signature but completes registration
  synchronously; validation exceptions are now thrown at invocation. Awaiting callers
  continue to observe them. Registration still constructs no components or provider.
- Authored ApplicationSetup.RegisterResources reuses AddApplicationResources so a
  callback failure or cancellation leaves the original service descriptors unchanged.
  A final cancellation check covers cancellation by the last callback. Factories remain
  lazy. As with existing staging, external side effects inside user callbacks cannot
  be rolled back; callbacks should register factories only.
- Updated configuration examples, package documentation, boundary checks and public API
  baseline. Historical migration inventories remain historical.

## Retained intentionally

Section-based ComponentRegistration and authored-definition ComponentRegistration
have different inputs. Both register native factories; neither is another runtime
container or activation layer. Unifying them would require unnecessary conversion.
Core's JSON model and authored definition helpers remain supported; this cleanup
does not remove them merely because optional configuration consumes them.

## Verification

- Full solution: 2,184 passing test executions across configured targets; zero failures.
- Release build: zero warnings/errors.
- Fresh isolated Core and Configuration package consumers passed on .NET 8 and .NET 10.
- API baseline and whitespace checks passed.
- Source scan found no removed definition-source types or BuildServiceProvider in src.

Evidence: artifacts/cleanup-review-20260920/post-revision-{tests,release-build,api,
core-consumer,configuration-consumer}.log. No commit, push or publication.

## Next decision

This cleanup slice is complete. Before Router configuration, settle the predicate input:
Router currently accepts FlowValue, whereas expressions may need original headers and
message IDs. Choose that real component boundary explicitly before writing its adapter.
Conditional-link expression file loading, output validation and automatic application
error collection remain deferred. Hot reload remains outside minimum Core.
