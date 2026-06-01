# FileSystem Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.FileSystem` as an independent component family.

The package is a category package, not a file-writer-only package. The first
node is `file.write`; future nodes can include `file.read`, `file.watch`, and
`directory.enumerate` without changing the package identity.

## Package Boundary

The FileSystem package owns:

- file system request and result contracts
- path resolution policy
- base directory handling
- absolute path allowance
- content and byte encoding behavior
- write modes
- per-message write errors
- focused tests

The package does not own:

- application logging or event contracts
- dashboards or editors
- product workspace models
- storage catalogs
- file picker UI behavior

Hosts can adapt their own request models and product events around the package
node while keeping file write mechanics package-owned.

## Implemented Node

```text
Node type: file.write
Input:     Input
Output:    Result
Errors:    node error stream
Options:   boundedCapacity, baseDirectory, allowAbsolutePaths,
           defaultEncoding
```

Request contract:

```text
FileWriteRequest
  Path
  Content
  Bytes
  Encoding
  Mode
  CreateDirectories
```

Result contract:

```text
FileWriteResult
  Path
  BytesWritten
  Mode
  WrittenAt
```

## Behavior

- Writes are ordered and asynchronous.
- `Bytes` takes precedence over `Content`.
- Relative paths are resolved under `baseDirectory` when configured.
- Relative paths that escape `baseDirectory` are rejected.
- Absolute paths require `allowAbsolutePaths`.
- Supported modes are overwrite, append, and create-new.
- Per-message failures emit `FlowError` and later messages continue.

## Verification

The first package test set covers:

- string content writes
- byte writes
- append ordering
- create-new IO failure recovery
- absolute path rejection
- base directory escape rejection
- per-request encoding
- unsupported encoding
- unsupported default encoding
- missing content
- diagnostics
- output completion

## Next Steps

1. Tag and publish `components-filesystem-v0.1.0-alpha.1` after this commit.
2. Migrate the first consuming application by keeping its current `file.writer`
   UX and delegating runtime writes to `file.write`.
3. Consider `file.read` next only after a real consumer proves the request and
   result shape.
