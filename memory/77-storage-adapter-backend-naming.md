# Storage Adapter Backend Naming

Date: 2026-06-02

## Decision

Refine the storage adapter package rule: each storage adapter package must map
to exactly one concrete persistence backend.

The base package remains:

```text
FluxFlow.Components.Storage
```

Reusable implementations remain under:

```text
FluxFlow.Components.Storage.*
```

But the suffix should identify a single backend rather than a broad storage
family.

## Why

Broad suffixes can become misleading. If two backends fit the same broad
category, putting both into one package would create:

- unnecessary dependencies for consumers
- harder versioning
- mixed configuration models
- larger test scope
- unclear ownership and release cadence

One backend per package keeps references explicit and keeps adapters small.

## Naming Examples

Good package shapes:

```text
FluxFlow.Components.Storage.FileSystem
FluxFlow.Components.Storage.<BackendName>
```

Avoid category-only names when the category can contain multiple backends.

## Current Status

No new storage adapter package should be created only because a category sounds
useful. Add the next adapter when a real workflow needs that concrete backend.

The file-system adapter is the first adapter package under the refined rule.
