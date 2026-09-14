# Resource objects and property addressing

Resources contains JSON values, not a tree of special Group definitions.
ResourceDefinition owns an immutable JsonElement. Removed ResourceGroupDefinition,
ResourceGroupBuilder and IResourceDefinitionContainerBuilder. Typed connection
definitions retain their activation contracts; nested containers are data.

The document reader accepts ordinary objects, arrays, primitives and null. The
shared connection enumerator walks object paths and stops at Type-bearing leaves.
MQTT, processing profiles and revision planning use this enumerator rather than
implementing their own group traversal. There are no special constant containers;
Type-bearing resource objects follow the ordinary connection-definition rules.

```csharp
var host = definition.GetResourceValue(
    "Resources.Connections.Messaging.Primary.Host").GetString();
var sameHost = definition.GetResourceValue(
    "Host", "Resources.Connections.Messaging.Primary").GetString();
var connectionDefinition = definition.GetResourceValue(
    "Resources.Connections.Messaging.Primary");
```

These read stored configuration, not live connection properties. No activation,
reflection or whole-document serialization is needed to read a path. Whole objects,
arrays and null retain their JSON types; numeric segments select array elements.
Section keywords Resources/Connections are normalized, while
user-defined names and property names remain ordinal. Dot-delimited addresses use
the existing segment grammar; TryReadJsonValue accepts explicit segments for data
properties containing dots. No implicit recursive name search is added.

ResolveResourcePaths accepts a full address, workflow-relative path, or object-relative
path with a full object context. Workflow-relative lookup tries the local path then
the application path. Explicit Resources chooses the application and
workflow.Resources chooses that workflow. Object-relative lookup stays in that exact
object. ResolveResource reuses the same resolver.

```csharp
application.AddResource("Connections.Messaging.Primary", "mqtt.broker",
    resource => resource.Set("Host", "localhost"));
```

The builder materializes ordinary JSON nesting from the path. Duplicate paths and
leaf/parent collisions fail before committing. Existing resource paths remain valid;
this step does not force migration of legacy application resource locations.

IConfiguration cannot distinguish every empty object from a null value. Only known
application/workflow structural objects are restored there; arbitrary nested nulls
are no longer guessed to be empty groups. Direct JSON preserves both values exactly.

The experimental [Constants implementation](2026-09-13-constants.md) has been
removed pending further design. Values can still be read from a definition through
the generic property-addressing API; this does not add substitution or runtime storage.
Workflow connection activation is implemented for direct Composition builds; see
[workflow connections](2026-09-13-workflow-connections.md). Private visibility and
revision-aware execution remain deferred.
