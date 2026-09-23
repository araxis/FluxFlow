# Current documentation cleanup

Corrected current guidance after the definition and direct-link refactors:

- Resource and component-registration guides no longer claim explicit document APIs remain.
- Source configuration describes the provider-owned singleton and persistent cursor
  across repeated Build calls, rather than claiming each Build creates a new Source.
- Repository/Core layouts omit the deleted Ports folder and describe current Configuration responsibilities.
- Configuration package dependencies include Mapping; repository adapter lists include Router.
- BuildApplicationAsync documentation correctly describes resolving the application from the provider.
- The public API section is titled Configuration setup rather than Optional definitions.
- The documentation index now prioritizes current guides and separates historical designs.

Historical guides, dated reviews and plans retain their original content. This was
not a claim that all historical snippets compile against the current branch.

Verification: checked package references and physical folders, scanned current package
guides for stale document-API assertions, verified relative links in the five edited
main guides/index files, and passed the whitespace diff check. No production code or
tests changed. No test rerun was needed; the latest runtime evidence is 1,796 passing
Release executions on .NET 8/10 in the direct-link review. No commit/publication.

Next decision: component setup coverage. Existing adapters cover Scheduler, Gate,
Source, Mapper and Router. Further adapter work should follow an explicit bounded
component requirement; another broad Application redesign is not justified by this audit.
