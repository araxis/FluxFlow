# Payloads Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.Payloads` as a separate component package with one
initial node:

- `payload.inspect`

The package is application-neutral. Hosts adapt their own envelopes into
`PayloadInspectionRequest` and keep any UI, dashboard, storage, or
transport-specific projection outside this package.

## Contracts

The first slice includes:

- `PayloadInspectionRequest`
- `PayloadInspectionResult`
- `PayloadKind`

`PayloadKind` covers empty, JSON object, JSON array, JSON scalar, XML,
base64, text, and binary payloads.

## Behavior

`payload.inspect` consumes bytes or text plus optional content type and
encoding hint metadata. Explicit encoding hints win; otherwise byte payloads
can use a content type `charset` value when present. The node emits
classification results with byte count, detected encoding, text preview,
formatted preview, parse error metadata, truncation flags, and base64 decoded
byte count when available.

Malformed JSON or XML stays on the result path with `ParseError` set. Unexpected
per-message failures emit structured `FlowError` values and the node continues
with later messages.

## Options

The node supports:

- `maxPreviewBytes`
- `maxFormattedChars`
- `detectBase64`
- `formatJson`
- `formatXml`
- `boundedCapacity`

## Verification

Focused coverage includes:

- JSON object, JSON array, and JSON scalar classification
- XML classification and formatting
- byte decoding from content type charset
- base64 detection
- empty and binary payloads
- parse error metadata
- preview truncation
- structured error output and continuation
- diagnostics
- registration and option validation

Planned release tag:

```text
components-payloads-v0.1.0-alpha.1
```
