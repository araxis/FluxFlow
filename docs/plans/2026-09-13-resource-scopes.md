# Resource definitions — application and workflow scope

## Structure and audience

Resources contains connection declarations and ordinary JSON resource objects.
Use the same structure at application level and inside a workflow. Keep configuration readable
and writable by Operations; Instances, Services and Dependencies were rejected as
replacement category names. Preserve flat component/link declarations.

Resource containers use ordinary nested JSON objects; the earlier Group concept
has been removed. There is no group discriminator or $value wrapper. Whole objects
and their properties are addressable as data. The mapping of
existing non-connection resources (such as clocks and processing settings) into
the agreed vocabulary also needs concrete examples before implementation.

## Location and addressing

A workflow-level definition establishes a location for relative addressing, not
an access restriction. Within that workflow, references can omit the workflow
name. Other workflows may reference it through a qualified address. Application
definitions are available throughout the application.

Reuse the established addressing approach: optional resource prefix when context
identifies it, nested object paths, exact paths rather than recursive name searches.
Relative addressing is a shared ApplicationAddress rule. CallFunction port
references reuse ResolvePort. Resource lookup
uses ResolveResourcePaths: local-first with application fallback for
relative names, explicit Resources prefix for the application and
workflow.Resources for cross-workflow access. See the concrete examples in
[the call component](2026-09-13-flow-functions.md). ResolveResourcePaths
now also accepts a resource-object scope. ApplicationDefinition.GetResourceValue
reads JSON through the shared TryReadJsonValue walker without activating resources.
See [addressing refactor](2026-09-13-resource-object-addressing.md).
Do not treat an address as permission to access another application.

## Values and calls

Constants are deferred while their purpose and design are reconsidered. There is
no runtime constant API or placeholder substitution. See the
[deferred idea](2026-09-13-constants.md). Ordinary JSON values and resource paths
remain available without a special constant mechanism.

CallFunction is an ordinary component with RequestTo, ReplyFrom and Timeout.
It emits the correlated reply through its own Output. Named function resources
and their registration/lookup APIs have been removed. C# MakeFunction remains a
small helper over existing ports, with caller-owned cancellation.

## Deferred access control

Do not implement private visibility, access checks or a private flag now. Defining
something inside a workflow does not make it private. Revisit optional explicit
privacy later; a flag with build-time validation is only a possible future design,
not a reserved schema field or current requirement. This supersedes the earlier
suggestion that workflow definitions must be private.


## Connection activation

Direct Composition construction now supports workflow resource declarations.
Local/fallback/qualified lookup and application-owned lifetime are implemented;
see [examples and registrar guidance](2026-09-13-workflow-connections.md).
Engine revision replacement remains deferred.
