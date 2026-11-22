# Validated addresses at the configuration boundary

## Result

The user requested address values instead of strings through configuration/link
registration, with malformed input rejected when an address is constructed.

Core.Addressing now exposes two focused immutable values, ComponentAddress and
PortAddress. Constructors are private; explicit factories delegate to ApplicationAddress
for the existing validation and scope rules. No implicit string conversion, new address
grammar, inheritance hierarchy or lookup mechanism was introduced. Value equality and
hashing reuse the underlying address value.

GetComponentAddress returns ComponentAddress from the original section path, including
host-selected roots. ComponentAddress.Port(name) constructs a PortAddress. DTO binding
passes the containing component to OneOrManyLinks.Read, which resolves each reference
to a full PortAddress before returning the DTO. LinkConfiguration.Address is therefore
typed and already resolved. Invalid scalar/object/list addresses report their property
or array position; multiple malformed entries retain aggregated diagnostics.

AddInputLinks(input, links), AddOutputLinks(output, links) and AddErrorLinks(output,
links) accept PortAddress. They no longer parse or resolve strings. Keyed-DI duplicate
identity uses typed source/target equality; the existing runtime Link API receives the
canonical Value. All bundled registrars and custom test/package consumers were migrated.

## Boundaries and compatibility

- Flat JSON syntax is unchanged: local, cross-workflow and System port references work.
- Link binding now requires an actual workflow component section. A standalone link
  property can be read with OneOrManyLinks.Read(section, componentAddress).
- GetComponentAddress, LinkConfiguration.Address and link-helper signatures are breaking
  development-preview API changes. There are no raw-string compatibility overloads.
- Existing native Core registration APIs and runtime lookup remain string-keyed; the
  registrar uses Value at that boundary. They were not broadly redesigned in this step.
- A valid address proves structure, not port existence or direction. Native registered
  blocks still supply those facts at Build. System output-only destination rejection
  stays in link registration as a routing rule, not syntax parsing.
- Input configuration names sources; Output/Error/Events configuration names targets.
  Error still filters the actual output and does not create a new physical port.
- Conditions, original message context, snapshots, transactional Add, delayed component
  construction, Events and input/output duplicate suppression are unchanged.

## Evidence

Full Release solution: 2,034 tests passed. Core: 257 per .NET 8/10 target;
Configuration: 287 per target. Added coverage for wrong address kinds, malformed
addresses, relative/full value equality and hashing, case sensitivity, system ports,
host-selected component scope and located binding failures. Existing routing,
deduplication and Error tests remain green. Public API baseline accepted and verified.
The isolated NuGet Configuration consumer passed on .NET 8.0.31 and .NET 10.0.12,
including typed binding, explicit registrar links and duplicate declarations from both ends.

Logs: artifacts/pre-setup-review-20260923/typed-address-{full,api,package}.log.
No commit, merge or publication performed.
