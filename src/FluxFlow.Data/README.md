# FluxFlow.Data

Transport-neutral data contracts for FluxFlow. This package has no dependency
on TPL Dataflow, the engine, composition, hosting, or any component family.

## Values

`FlowValue` is a deeply immutable discriminated value used by dynamic
components and message headers. It keeps integer, decimal, and floating-point
numbers distinct; copies mutable input at construction; compares object keys
with ordinal semantics; and treats object property order as insignificant.

`FlowValueCanonicalJson` writes a deterministic, kind-tagged representation.
The tagged format is intended for lossless persistence and hashing. It is not
the natural JSON projection used by mapping components.

## Content

`FlowContent` preserves ingress bytes once together with optional content type
and encoding metadata. `ReadAsFlowValue(...)` performs lazy, thread-safe decode
through a `FlowContentCodecCatalog`, caching either the value or the failure.

The default catalog handles JSON, structured `+json` media types, `text/*`, and
binary fallback. Additional media families, including XML, are registered
explicitly by the package or host that owns their mapping convention.

## Results

`IFlowResult`, `FlowResult<T>`, and `FlowError` define the vNext convention for
expected operation failures on a component's normal output. `IsError` is
derived from the presence of `Error`; it is not independently mutable.

This package does not define node ports, runtime error streams, routing,
resource ownership, serialization components, or implicit conversions between
linked CLR payload types.
