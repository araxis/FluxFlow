# Source samples from JSON text or files

The user approved configuration readability improvements only where concrete needs
justify them. This step addresses escaped sample JSON in Source configuration.
Duration syntax, file-backed link conditions and unknown-property diagnostics remain
proposals; no additional configuration framework was introduced.

SourceConfiguration.Values now uses JsonSource. Plain JSON text continues to work;
inline: explicitly selects text, and file: selects a file. The type supports native
scalar binding and returns a standalone JsonElement. JSON syntax and scalar types
are handled by System.Text.Json. JsonSource does not require an array; the Source
registrar performs that component-specific check.

AddSource(baseDirectory) captures the host's file root, defaulting to
AppContext.BaseDirectory. Source reads both inline/file samples during registration,
matching its previous snapshot contract. Files can then change or disappear without
changing constructed instances. Invalid JSON, non-array samples and missing files
fail staged registration atomically. Component creation remains deferred; provider
identity, message lineage and Source cursor behavior are unchanged.

Migration: rename ValuesJson to Values. No alias was retained. AddSource() callers
remain source-compatible; rebuild callers for its optional-argument signature.
No Core/component dependency or runtime type changed. ExpressionSource retains its
existing Build-time behavior. No automatic extension detection, Base64 decoding,
generic source base, resolver registry, source cache or hot reload was added.

Validation:

- Configuration: 187 passing cases on each of .NET 8 and .NET 10.
- Full Release suite: 1,872 passing executions; public API baseline updated.
- Isolated Configuration/engine package consumer passed on .NET 8 and .NET 10,
  exercising inline: JsonSource samples through the running workflow.
- Tests cover exact heterogeneous JSON, native scalar binding, explicit inline/file
  semantics, UTF-8 BOM and Unicode files, file deletion after registration, independent
  providers, repeated Build, malformed/missing sources and registration atomicity.
- Existing Source integration tests preserve lineage, exhaustion and deferred creation.
- Logs: artifacts/cleanup-review-20260920/json-source-{tests,api,full-tests,package}.log.

No commit/publication in this step.
