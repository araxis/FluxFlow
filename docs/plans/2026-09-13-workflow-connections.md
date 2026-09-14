# Connections inside workflows

Direct Composition BuildApplicationAsync now activates resources declared at both
application and workflow level. A workflow establishes an address location, not
privacy or a separate lifetime. Constructed connections belong to the same
application resource scope and are disposed after its components. Host services
remain borrowed.

~~~json
{
  "Resources": {
    "Connections": {
      "broker": { "Type": "mqtt.broker", "Host": "shared-broker" }
    }
  },
  "Workflows": {
    "orders": {
      "Resources": {
        "Connections": {
          "client": {
            "Type": "mqtt.client",
            "ClientId": "orders",
            "Broker": "Connections.broker"
          }
        }
      },
      "send": { "Type": "mqtt.publish", "Client": "Connections.client" }
    },
    "shipping": {
      "Resources": {
        "Connections": {
          "broker": { "Type": "mqtt.broker", "Host": "shipping-broker" },
          "client": {
            "Type": "mqtt.client",
            "ClientId": "shipping",
            "Broker": "Connections.broker"
          }
        }
      },
      "send": { "Type": "mqtt.publish", "Client": "Connections.client" }
    }
  }
}
~~~

The host registers MQTT components and supplies its transport as usual. Orders
uses the application broker. Shipping uses its own broker. Each workflow's
component resolves its local client; normal Input/Output/link syntax is unchanged.

| Reference inside shipping | Resolution |
| --- | --- |
| Connections.client | shipping.Resources.Connections.client, then Resources.Connections.client |
| Resources.Connections.broker | Application broker only |
| orders.Resources.Connections.client | Orders client explicitly |

Resource names and ordinary JSON properties remain case-sensitive. Structural
Resources/Connections prefixes accept the established casing variants. Fully
qualified paths never use workflow fallback. No recursive search or group model
is added. MQTT broker, subscriptions, retry policy, and host-owned
credential/certificate references use the same workflow path rules. A declared
MQTT dependency with the wrong type fails instead of falling back.

C# definition uses the existing resource contract and container implementation:

~~~csharp
var builder = new ApplicationDefinitionBuilder();
var broker = builder.AddResource("Connections.broker", MqttResources.Broker,
    options => options.Host = "shared-broker");
var orders = builder.AddWorkflow("orders");
var client = orders.AddResource("Connections.client", MqttResources.Client,
    options => {
        options.ClientId = "orders";
        options.Broker = broker;
    });
// client.Address is orders.Resources.Connections.client.
// Pass this handle to a component's ordinary resource configuration.
~~~

Workflow AddResource supports raw types, typed resource handles and existing
resource contracts. Handles are fully qualified; duplicate paths and builder
freeze retain existing behavior. Portable setup export preserves workflow Resources.
As before, setups carrying runtime activation contracts cannot be exported through
ToDefinition; they retain their data in setup.Definition and execute directly.
There is no replacement builder or independent workflow service provider.

Custom resource registrars should enumerate
ResourceInstanceDefinition.Enumerate(context.Definition). This overload returns
full ApplicationAddress keys across application and workflow declarations.
The dictionary overload still enumerates only the supplied resource map.
Registrars are invoked once per existing registration identity, not per workflow.
Custom registrars manually inspecting only definition.Resources must adopt the
new overload to support workflow declarations. Built-in processing profiles and
MQTT registration already use it.

Plain FlowJson.BuildAsync still requires Composition for activated resources.
Engine revision hosting/hot reload still rejects workflow resources explicitly:
replacement and ownership across revisions remain a separate step.
Constants and private visibility remain deferred.

Validation includes local/shared/cross-workflow requests, one construction and
disposal per connection, cleanup after a failed build, C# handle/export behavior,
and MQTT configuration/dependency binding without opening external connections.
