using FluxFlow.Core.Addressing;

namespace FluxFlow.Components.Mqtt.Composition;

public interface IMqttInlineSecretPolicy
{
    bool IsAllowed(ApplicationAddress client, string propertyName);
}
