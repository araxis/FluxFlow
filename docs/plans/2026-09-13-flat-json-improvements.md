# Flat JSON improvements

The user explicitly selected the existing flat format for Operations staff who
read and edit configuration without access to source code. Keep input- and output-
side links, string/object/array forms, mixed conditional fanout and optional groups.
Do not introduce Components/Options/Links containers as a replacement structure.

Implemented:

- Preserve insertion order in the immutable-by-interface model using private copied
  dictionaries exposed read-only. Parsed definitions also retain section/Type/
  ExpressionEngine positions, so serialization does not move these fields to the
  beginning. Nested JsonElement objects and arrays write in their original order.
- Read structural field names case-insensitively and normalize model fields to
  canonical spelling. Standard port properties and output policies normalize too.
  Link object fields and delivery names accept case variants; mixed duplicates
  are rejected by their parser. Arbitrary settings, identifiers and expression
  text are not recursively lowercased. Link object spelling/order is retained
  in the portable JSON value and interpreted by the link parser.
- Resources is optional on input. A parsed document without it stays without it
  on serialization. Programmatic new definitions retain their existing explicit
  empty Resources output. Ungrouped and nested resources remain valid.
- ApplicationAddress.ResolveResource/TryResolveResource accept client,
  group.client, and Resources.group.client (prefix insensitive; path exact).
  No recursive leaf-name search is performed. Component dependency lookup prefers
  the canonical resource key, with the prior explicit host key as fallback. MQTT
  resource-to-resource references use the same path resolver. Storage and Sessions
  adapters now use the shared dependency lookup; their execution/design is unchanged.
- Required-resource errors include the component, property, original reference,
  resolved path and expected service type. Ordinary invalid JSON and duplicate
  structural fields remain errors.

Tests verify round-trip order at all document levels, stable second serialization,
case preservation of options/type identifiers, omitted Resources, real lowercase
link execution, duplicate structural aliases, exact resource paths and missing
resources. Component activation tests exercise all reference forms and reject
accidental search in other groups. MQTT tests execute resource registration with
full, lowercase-prefix and prefix-free references.

Validation: JSON 113 passed, Composition 272, MQTT Composition 37, Engine 537,
Core 150 per net8/net10, Storage Composition 2. Public source API baseline reviewed
and normal verification passed 2. Full Release solution build passed with zero
warnings/errors. Initial failures were obsolete strict-casing and sorted-output
expectations, updated to the agreed contract. Evidence under artifacts/json-improvements.

Scope limits: this preserves JSON member order, not original whitespace/comments.
Custom port names and address path segments remain case-sensitive. JSON Functions
are not introduced in this step. Source-generated message serializers, Sessions
redesign and external telemetry backends remain outside this change.
