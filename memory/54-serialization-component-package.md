# Serialization Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.Serialization` as a separate component package with
six initial nodes:

- `json.parse`
- `json.stringify`
- `text.encode`
- `text.decode`
- `base64.encode`
- `base64.decode`

The package is application-neutral. It owns conversion behavior only. Hosts
adapt their own envelopes, requests, files, messages, or HTTP bodies into the
package request contracts.

## Contracts

The first slice includes:

- `JsonParseRequest`
- `JsonParseResult`
- `JsonStringifyRequest`
- `JsonStringifyResult`
- `TextEncodeRequest`
- `TextEncodeResult`
- `TextDecodeRequest`
- `TextDecodeResult`
- `Base64EncodeRequest`
- `Base64EncodeResult`
- `Base64DecodeRequest`
- `Base64DecodeResult`

## Behavior

Each node has one `Input`, one `Output`, and one `Errors` port. Per-message
conversion failures emit structured `FlowError` values and the node continues
with later messages.

JSON parsing supports optional trailing commas and skipped comments. JSON
stringifying supports configured or per-request indentation. Text and base64
operations use a configurable default encoding with per-request overrides.

## Options

Each node supports:

- `boundedCapacity`
- `defaultEncoding`
- `maxInputBytes`
- `maxOutputBytes`
- `writeIndented`
- `allowTrailingCommas`
- `skipComments`

## Verification

Focused coverage includes:

- JSON parsing
- JSON parse failure continuation
- JSON stringifying
- text encoding
- text decoding with byte order mark handling
- base64 encoding
- base64 decoding with optional text output
- oversized output errors and continuation
- unsupported encoding errors
- diagnostics
- registration and option validation

Planned release tag:

```text
components-serialization-v0.1.0-alpha.1
```
