# Journal Component Package

## Status

`FluxFlow.Components.Journal` `0.1.0-alpha.1` adds neutral event journal
contracts and an in-memory store.

## Scope

- `JournalRecord` normalizes runtime events into queryable records.
- `JournalQuery` supports type, status, source, workflow, node, component,
  subject/channel prefix, exclusion prefix, severity, level, attributes, and
  time-range filters.
- `JournalQueryMatcher` keeps filtering reusable outside the in-memory store.
- `IJournalStore` defines append, query, and prune operations.
- `InMemoryJournalStore` provides append-order storage, paging, duplicate id
  protection, deterministic age-based retention, and max-record pruning.
- `FlowEventJournalRecordMapper` maps the engine runtime event contract into a
  journal record without adding host-specific fields.

## Decisions

- No runtime node was added in this slice. The package starts with contracts and
  storage primitives because hosts can use them directly and future nodes can
  build on the same surface.
- Age-based retention requires a caller-provided reference timestamp. That keeps
  behavior deterministic and avoids hidden ambient time.
- The in-memory store rejects duplicate record ids so appenders notice identity
  mistakes early.
- The package references the engine only for the neutral `FlowEvent` adapter.

## Verification

- `dotnet test tests\FluxFlow.Components.Journal.Tests\FluxFlow.Components.Journal.Tests.csproj --configuration Release`
- `dotnet build FluxFlow.sln --configuration Release`
- `dotnet test FluxFlow.sln --configuration Release --no-build`
- `dotnet pack src\FluxFlow.Components.Journal\FluxFlow.Components.Journal.csproj --configuration Release --no-build --output artifacts\packages`
- Release workflow run `26900270288` completed successfully for
  `components-journal-v0.1.0-alpha.1`.
- Public-feed restore/run smoke test restored
  `FluxFlow.Components.Journal` `0.1.0-alpha.1` into a fresh console app and
  returned `1:evt-1:False`.

## History

- The first tag run failed because `eng/packages.json` did not yet contain the
  `components-journal` release prefix. Added the package map entry and moved the
  tag forward.
- The second tag run failed because `CHANGELOG.md` did not yet contain release
  notes for `FluxFlow.Components.Journal` `0.1.0-alpha.1`. Added the changelog
  entry, validated the release-notes script locally, and moved the tag forward.

## Next

- Continue the broader component maturity backlog. Current likely next target:
  storage adapter hardening or the next runtime package from the backlog.
