# Remove the Core content contract

Removed FlowContent and FlowContentJsonConverter from minimum Core. Source search
found no execution consumer: the remaining callers were binary workload fixtures
and tests of that removed API. The removed files are saved under
artifacts/remove-content-20260915/before/.

Binary workloads now serialize their own plain JSON object using System.Text.Json.
They retain formatVersion, Base64 bytes, contentType and encoding. Byte equality,
empty payload and metadata checks remain. PayloadSizeDeliveryBenchmarks still
checks that clearing the source array does not change the published snapshot.
No benchmark timing/performance claim is made.

The nine FlowContent-specific test cases on each of net8/net10 were removed with
the API (18 cases total); no routing or lifecycle tests were removed. The release
boundary test now checks FlowValue and FlowError. Public source baseline reviewed
and updated: removing these public types is a breaking preview change. Existing
consumers must own their former content contract/converter in their module.

This is only the first step of the component-authoring plan. FlowMessage<T> remains
unconstrained today; its runtime/port consumers require a coordinated migration.
The accepted FlowValue constraint and shared message implementation are pending,
as is simplifying the base classes. No claim of completing that refactor is made.

Validation: 2,369 tests passed in seven reports, zero failed/skipped (2,387 minus
the 18 removed API-specific cases). Fresh Core-only package consumers passed on
.NET 8.0.31 and .NET 10.0.12. Release build passed. Evidence is under
artifacts/remove-content-20260915/: verification.json, tests/, build.log and
consumer.log; the fresh consumer/package is in consumer-check/. No publication,
commit or push occurred.
