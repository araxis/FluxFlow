# FileSystem Read Component

Date: 2026-06-01

## Decision

Add `file.read` to `FluxFlow.Components.FileSystem` and move shared path
resolution into package-owned helper code used by both read and write nodes.

This keeps the package as a file system component family instead of a
single-node writer package.

## Implemented Node

```text
Node type: file.read
Input:     Input
Output:    Result
Errors:    node error stream
Options:   boundedCapacity, baseDirectory, allowAbsolutePaths,
           defaultEncoding, maxBytes
```

Request contract:

```text
FileReadRequest
  Path
  Encoding
  ReadAs
```

Result contract:

```text
FileReadResult
  Path
  Content
  Bytes
  Encoding
  BytesRead
  ReadAs
  ReadAt
```

## Behavior

- Reads are ordered and asynchronous.
- `ReadAs = Text` returns decoded content.
- `ReadAs = Bytes` returns raw bytes.
- Text reads use request encoding first, then `defaultEncoding`.
- Relative paths are resolved under `baseDirectory` when configured.
- Relative paths that escape `baseDirectory` are rejected.
- Absolute paths require `allowAbsolutePaths`.
- `maxBytes` rejects files that are too large before emitting results.
- Per-message failures emit `FlowError` and later messages continue.

## Shared Path Policy

`file.write` and `file.read` now use the same internal path resolver. The
resolver owns:

- empty path rejection
- absolute path permission checks
- relative path resolution against `baseDirectory`
- base directory escape checks
- invalid path wrapping into node-specific error codes

## Verification

The read test set covers:

- text reads
- byte reads
- missing-file recovery
- absolute path rejection
- base directory escape rejection
- per-request encoding
- unsupported encoding
- max byte rejection
- diagnostics
- output completion
- invalid bounded capacity
- unsupported default encoding
- invalid max byte options

Existing write tests still cover the writer behavior after the path-policy
extraction.

## Next Steps

1. Tag and publish `components-filesystem-v0.2.0-alpha.1` after this commit.
2. Keep `file.watch` deferred until the event shape and lifecycle rules are
   clearer.
3. Consider a future shared helper only after another component family needs
   the same path policy.
