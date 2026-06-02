# Storage Local Package Unlist

Date: 2026-06-02

## Decision

Retire the old storage adapter package id from public package discovery:

```text
FluxFlow.Components.Storage.Local
```

The replacement package is:

```text
FluxFlow.Components.Storage.FileSystem
```

## Versions To Unlist

```text
0.1.0-alpha.1
0.2.0-alpha.1
```

## Why

The old package id used a location-based name. That is ambiguous because file
system, LiteDB, SQLite, and other embedded/local persistence adapters can all
run locally.

The package family now uses concrete backend names:

```text
FluxFlow.Components.Storage.<BackendName>
```

## Maintenance Path

Use the manual package maintenance workflow for package unlisting. It keeps the
package-feed key inside the repository secret path and limits package operations
to the `FluxFlow.` package family.

## Result

Workflow run `26807164052` completed successfully on 2026-06-02.

The package feed accepted unlist calls for both old versions. Public package
search returned zero results for the old package id immediately after the run.
Some package metadata endpoints can continue returning cached version metadata
for a while, but the package is removed from public discovery.
