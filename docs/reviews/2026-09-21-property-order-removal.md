# Remove document property-order tracking

The user explicitly withdrew the earlier requirement to preserve handwritten JSON
member placement in Core. Future editor metadata belongs to a separate design.

Removed internal PropertyOrder from ApplicationDefinition, WorkflowDefinition and
ComponentDefinition, plus the converter's read-time order snapshots and write-time
replay. Serialization now writes the model directly: nonempty Resources first,
then Workflows or components, and Type before component settings. Empty Resources
are omitted consistently for both read and directly constructed models. Original
member placement and the presence of empty Resources sections are not retained.
JSON payload values and array ordering remain intact; no deliberate sorting or
new editor metadata layer was added. DocumentValidation is unchanged.

Existing serializer tests were updated for the new output contract while retaining
content, structural-name normalization, array and deterministic round-trip checks.
Ordinary IConfiguration registration is unchanged.

Validation: Release Core tests passed 417 per target, Configuration tests 112 per
target on .NET 8 and .NET 10 (1,058 total executions). Logs:

- artifacts/cleanup-review-20260920/property-order-core.log
- artifacts/cleanup-review-20260920/property-order-configuration.log

No public API signatures changed. Full solution/package validation was not repeated
for this internal serializer change. No commit or publication.
