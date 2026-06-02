# Storage FileSystem Adapter Rename

Date: 2026-06-02

## Decision

Rename the first persisted storage adapter to:

```text
FluxFlow.Components.Storage.FileSystem
```

Use backend-based public API names:

- `FileSystemStorageStore`
- `FileSystemStorageStoreOptions`
- `FileSystemStorageStoreFactory`
- `UseFileSystemStorage(...)`

## Why

The previous name described where the store runs, not how it persists records.
That became misleading because many different persistence backends can run on
the same machine.

The package writes one JSON file per `StorageRecord`, so `FileSystem` is the
concrete backend name.

## Release Shape

Because the package id changed, the file-system adapter starts as:

```text
0.1.0-alpha.1
```

The package includes the already implemented put, get, query, and delete store
behavior.

## Follow-Up Rule

Future storage adapters should follow the same shape:

```text
FluxFlow.Components.Storage.<BackendName>
```

Do not use location-based names for persistence adapters.
