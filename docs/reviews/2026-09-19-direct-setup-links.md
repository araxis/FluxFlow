# Apply setup links without a document export roundtrip

Previously, FlowJson.ApplyLinks(ports, setup) called the portable export projection:
it copied every workflow/component property dictionary, merged C# links into JSON
properties, constructed another ApplicationDefinition and parsed those links again.
Execution therefore depended on a serialization operation unrelated to creating routes.

FlowJson now passes resolved setup declarations directly to the existing DocumentLinks
consumer. It combines original JSON declarations and setup additions at each declared
port, then uses the same condition compiler and address-based link helpers. ToDefinition
still performs portable export; its Project helper is now private to that export class.

No production class, interface, public API or replacement builder was added. Existing
ApplicationLinkDeclarationProjection objects still validate resolved declarations;
this change removes the full-document export roundtrip, not every intermediate value.
DocumentLinks gains a small amount of local merging logic to preserve declaration order.
This is a responsibility/dependency simplification, not a claimed line-count reduction
or measured performance improvement.

## Preserved behavior

- Existing document property order followed by newly declared ports in setup order;
  document declarations precede additions on the same port. Native predicates remain last.
- Both input-side and output-side declarations, cross-workflow addresses, conditions,
  parameters on existing JSON links, fallback, delivery policies and rejection callbacks.
- Ordinary identical-declaration merging and conflicting-policy errors. Revision
  duplicate-link semantics remain separate and unchanged.
- Validation of malformed existing declarations even when the declared custom port
  has not been registered. The original setup document is not mutated.
- Build owns partial-construction cleanup; route execution and lifecycle are unchanged.

## Verification

Seven new test cases run on net8/net10. They compare direct setup and exported-document
execution, check mixed declaration/predicate order, deduplication, fallback, conflicting
policies, malformed declarations and cleanup. The package-only consumer now also builds
and runs input-side setup links alongside its JSON and address-link scenarios.

Release build: zero warnings/errors. Final suite: 2,539 passed, zero failed/skipped,
29 TRX reports. Fresh Core package 0.0.0-local.20260919204954 passed its package-only
consumer on .NET 8.0.31 and .NET 10.0.12. Evidence is recorded in
artifacts/direct-setup-links/verification.json. The source API baseline scanner counts
public methods on internal types; its hash changed for DocumentLinks.Apply's internal
implementation signature. No exported signature changed. The reviewed baseline was
updated and included in the final full-suite run.

No commit, push or publication. Endpoint policies and completion coordination remain;
further simplification must identify a concrete duplicated responsibility rather than
moving that behavior to another class.
