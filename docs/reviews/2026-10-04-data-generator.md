# DataGenerator

## Scope and structure

FluxFlow.Components.DataGeneration is a separate component library using
Jsonata.Net.Native.SystemTextJson 3.0.0 and Bogus 35.6.5. DataGenerator owns native
Input, Output and Events blocks and uses the existing FlowLifetime and synchronous
processing helper. Core, Mapper and both existing expression engines are unchanged.

Configuration/Components/DataGeneration contains DataGeneratorConfiguration and
AddDataGenerator. The DTO uses ExpressionSource and the shared component link
properties. Registration stores deferred factories; Build reads expression files
and constructs/parses the generator. No separate configuration project is added.

## Behavior

Each ordinary input evaluates the expression once, combining payload/context with
35 automatically bound Bogus functions for random values, dates/times, names,
contact/location details, text and business data. The output is a new owned FlowValue
and a derived envelope retaining the existing lineage contract. Arrays are one
payload; explicit null is valid. Undefined and runtime expression/function errors
produce normal FlowError outputs. Upstream errors bypass evaluation.

BogusFunctions contains explicit delegate bindings to its component-owned Faker.
GeneratorExpression creates a fresh native evaluation environment per input and
binds the ordinary message context. No function catalog, new expression interface
or global enrichment policy is introduced. Ordinary Mapper expressions do not gain
Bogus functions. Additional generator bindings can be added at this local boundary.

Generation is sequential to avoid concurrent use of Faker state. The optional native
Execution options must retain MaxDegreeOfParallelism=1. Cancellation is cooperative
before/after synchronous evaluation. There is no seed setting, timing setting,
background production or Engine selector. Scheduler owns cadence. Locale defaults
to en, and output capacity defaults to 128.

## Verification

- 38 targeted test executions across .NET 8/10 cover every built-in function,
  input/context merging, lineage, arrays/null, upstream errors, invalid ranges,
  runtime recovery, cancellation and construction validation.
- Configuration tests cover deferred file reading, settings snapshots, repeated
  Build reuse, both link declaration directions and deduplication, Events, atomic
  validation and located Build failures.
- Full Release suite: 2,275 passed; one package convention test identified a missing
  components tag. Fixed the metadata and reran both package convention tests, which
  passed. This completes verification of the 2,276-test suite with no build warnings.
- API baseline changes are limited to the new package and Configuration additions.
- Isolated candidate packages built for .NET 8/10. The Configuration consumer uses
  DataGenerator inside its scheduled flow and passed on both runtimes. Its expected
  link count was updated for the added stage; duplicate-link checks remain intact.

No commit or publication was performed.

## Expanded generator functions

Reviewed the pinned Bogus 35.6.5 APIs and added 27 explicit bindings, including
date-only ranges, timestamp ranges, time-of-day ranges, durations, past/future and
recent/soon timestamps, month/weekday names, identity/contact/location data, company
and product names, and Lorem text. The runtime README lists all 35 signatures.
This does not expose the entire Bogus API or introduce a reflection dispatcher.

Dates and times return invariant JSON strings. Timestamp bounds require an explicit
offset and are converted to UTC before Bogus compares their wall-clock ticks.
Date-only generation selects whole day numbers uniformly, including both bounds.
Time ranges do not wrap midnight. Relative ranges require positive years/days and
use UTC now; malformed/reversed bounds and negative durations follow the existing
processing-error path. No configuration options or public C# APIs changed.

Expanded targeted verification: 74 passing executions across .NET 8/10, including
every binding, leap dates, equal bounds, fractional precision, differing UTC offsets,
three host cultures, invalid arguments and recovery after failure.
Full solution Release verification after the expansion: 2,312 passing test executions
across .NET 8/10, with no build warnings. The existing package/API checks also pass.
